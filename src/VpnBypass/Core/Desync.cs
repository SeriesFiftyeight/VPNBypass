using System.Net.Sockets;
using System.Text;

namespace VpnBypass.Core;

/// <summary>
/// User-mode DPI evasion. DPI boxes read the hostname from the first packet of a connection
/// (TLS SNI or HTTP Host). We reshape that packet so the hostname never appears whole in one
/// TLS record / TCP segment, while the server still reassembles a perfectly valid stream.
/// </summary>
public static class Desync
{
    /// <summary>Offset of the SNI hostname inside a TLS ClientHello record, or -1.</summary>
    public static int FindSni(ReadOnlySpan<byte> d)
    {
        // record(5) + handshake type(1) + length(3)
        if (d.Length < 48 || d[0] != 0x16 || d[5] != 0x01) return -1;
        int p = 9 + 2 + 32;                         // client version + random
        if (p >= d.Length) return -1;
        p += 1 + d[p];                              // session id
        if (p + 2 > d.Length) return -1;
        p += 2 + (d[p] << 8 | d[p + 1]);            // cipher suites
        if (p >= d.Length) return -1;
        p += 1 + d[p];                              // compression methods
        if (p + 2 > d.Length) return -1;
        int end = Math.Min(d.Length, p + 2 + (d[p] << 8 | d[p + 1]));
        p += 2;
        while (p + 4 <= end)
        {
            int type = d[p] << 8 | d[p + 1];
            int len = d[p + 2] << 8 | d[p + 3];
            if (type == 0)
            {
                // ext header(4) + list length(2) + name type(1) + name length(2)
                int name = p + 9;
                return name < end ? name : -1;
            }
            p += 4 + len;
        }
        return -1;
    }

    /// <summary>Offset of the Host header value in an HTTP request, or -1.</summary>
    public static int FindHttpHost(byte[] d, int length)
    {
        var head = Encoding.ASCII.GetString(d, 0, Math.Min(length, 8192));
        int i = head.IndexOf("\r\nHost:", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return -1;
        i += 7;
        while (i < head.Length && head[i] == ' ') i++;
        return i;
    }

    /// <summary>Rewrites one TLS record into two records split at <paramref name="at"/>.</summary>
    public static byte[] SplitTlsRecord(byte[] d, int at)
    {
        int recLen = d[3] << 8 | d[4];
        if (5 + recLen > d.Length || at <= 5 || at >= 5 + recLen) return d;

        int len1 = at - 5, len2 = recLen - len1;
        var r = new byte[d.Length + 5];
        d.AsSpan(0, 3).CopyTo(r);
        r[3] = (byte)(len1 >> 8); r[4] = (byte)len1;
        d.AsSpan(5, len1).CopyTo(r.AsSpan(5));
        d.AsSpan(0, 3).CopyTo(r.AsSpan(at));
        r[at + 3] = (byte)(len2 >> 8); r[at + 4] = (byte)len2;
        d.AsSpan(at, d.Length - at).CopyTo(r.AsSpan(at + 5));
        return r;
    }

    /// <summary>Sends the first client payload to the server using the profile's tricks.</summary>
    public static async Task SendFirstAsync(Socket s, byte[] data, BypassProfile p, CancellationToken ct)
    {
        int cut;
        int sni = FindSni(data);
        if (sni > 0)
        {
            // Cut right after the first hostname character: "d|iscord.com".
            cut = sni + 1;
            if (p.TlsRecordSplit) data = SplitTlsRecord(data, cut);
        }
        else
        {
            int host = FindHttpHost(data, data.Length);
            if (host < 0)
            {
                await SendAllAsync(s, data, ct);
                return;
            }
            cut = host + 1;
        }

        var points = new SortedSet<int>();
        if (p.SplitFirstByte) points.Add(1);
        if (p.SplitAtSni || p.Disorder) points.Add(cut);
        points.RemoveWhere(x => x <= 0 || x >= data.Length);

        int start = 0, index = 0;
        foreach (int point in points.Append(data.Length))
        {
            var segment = data.AsMemory(start, point - start);
            if (p.Disorder && index == 0) await SendLowTtlAsync(s, segment, ct);
            else await SendAllAsync(s, segment, ct);
            start = point;
            index++;
        }
    }

    private static async Task SendLowTtlAsync(Socket s, ReadOnlyMemory<byte> segment, CancellationToken ct)
    {
        short ttl = s.Ttl;
        try
        {
            s.Ttl = 1;
            await SendAllAsync(s, segment, ct);
            // Let the stack emit the segment before the TTL goes back up.
            await Task.Delay(1, ct);
        }
        finally
        {
            s.Ttl = ttl;
        }
    }

    private static async Task SendAllAsync(Socket s, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        while (data.Length > 0)
        {
            int n = await s.SendAsync(data, SocketFlags.None, ct);
            data = data[n..];
        }
    }
}
