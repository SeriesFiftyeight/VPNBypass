using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;

namespace VpnBypass.Core;

/// <summary>
/// Own DNS-over-HTTPS resolver (RFC 8484 wire format). Bypasses ISP DNS poisoning and
/// never depends on the system resolver, so it also works on IPv6-only links.
/// </summary>
public sealed class DohResolver
{
    public sealed record Provider(string Id, string Name, string Host, IPAddress[] Addresses);

    public static IReadOnlyList<Provider> Providers { get; } =
    [
        new("cloudflare", "Cloudflare", "cloudflare-dns.com",
            [IPAddress.Parse("2606:4700:4700::1111"), IPAddress.Parse("2606:4700:4700::1001"), IPAddress.Parse("1.1.1.1"), IPAddress.Parse("1.0.0.1")]),
        new("google", "Google", "dns.google",
            [IPAddress.Parse("2001:4860:4860::8888"), IPAddress.Parse("2001:4860:4860::8844"), IPAddress.Parse("8.8.8.8"), IPAddress.Parse("8.8.4.4")]),
        new("quad9", "Quad9", "dns.quad9.net",
            [IPAddress.Parse("2620:fe::fe"), IPAddress.Parse("2620:fe::9"), IPAddress.Parse("9.9.9.9"), IPAddress.Parse("149.112.112.112")]),
        new("adguard", "AdGuard", "dns.adguard-dns.com",
            [IPAddress.Parse("2a10:50c0::ad1:ff"), IPAddress.Parse("2a10:50c0::ad2:ff"), IPAddress.Parse("94.140.14.14"), IPAddress.Parse("94.140.15.15")])
    ];

    public static DohResolver Instance { get; } = new();

    private readonly ConcurrentDictionary<string, (IPAddress[] Ips, DateTime Expires)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, HttpClient> _clients = new();

    public static Provider Get(string id) => Providers.FirstOrDefault(p => p.Id == id) ?? Providers[0];

    public void ClearCache() => _cache.Clear();

    /// <summary>Resolves A and AAAA records. Falls back to the system resolver if DoH is unreachable.</summary>
    public async Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct = default)
    {
        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal)) return [literal];
        if (_cache.TryGetValue(host, out var hit) && hit.Expires > DateTime.UtcNow) return hit.Ips;

        var provider = Get(AppSettings.Current.DnsProvider);
        try
        {
            var v6 = QueryAsync(provider, host, 28, ct);
            var v4 = QueryAsync(provider, host, 1, ct);
            var (ips6, ttl6) = await v6;
            var (ips4, ttl4) = await v4;
            var all = ips4.Concat(ips6).ToArray();
            if (all.Length > 0)
            {
                int ttl = Math.Clamp(Math.Min(ips4.Length > 0 ? ttl4 : int.MaxValue, ips6.Length > 0 ? ttl6 : int.MaxValue), 60, 3600);
                _cache[host] = (all, DateTime.UtcNow.AddSeconds(ttl));
                return all;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            // DoH unreachable; fall through to the system resolver.
        }

        try { return await Dns.GetHostAddressesAsync(host, ct); }
        catch { return []; }
    }

    private async Task<(IPAddress[] Ips, int Ttl)> QueryAsync(Provider p, string host, ushort type, CancellationToken ct)
    {
        using var content = new ByteArrayContent(BuildQuery(host, type));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://{p.Host}/dns-query") { Content = content };
        req.Headers.Accept.ParseAdd("application/dns-message");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        using var resp = await Client(p).SendAsync(req, timeout.Token);
        resp.EnsureSuccessStatusCode();
        return ParseAnswer(await resp.Content.ReadAsByteArrayAsync(timeout.Token), type);
    }

    private HttpClient Client(Provider p) => _clients.GetOrAdd(p.Id, _ => new HttpClient(new SocketsHttpHandler
    {
        UseProxy = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        // Dial the provider's fixed IPs ourselves: no chicken-and-egg with system DNS.
        ConnectCallback = async (_, ct) =>
        {
            foreach (var ip in NetworkState.Order(p.Addresses))
            {
                var s = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    t.CancelAfter(2500);
                    await s.ConnectAsync(new IPEndPoint(ip, 443), t.Token);
                    return new NetworkStream(s, ownsSocket: true);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { s.Dispose(); throw; }
                catch { s.Dispose(); }
            }
            throw new HttpRequestException($"{p.Name} DoH unreachable");
        }
    })
    {
        DefaultRequestVersion = HttpVersion.Version20,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
    });

    private static byte[] BuildQuery(string host, ushort type)
    {
        var labels = host.TrimEnd('.').Split('.');
        var buf = new byte[12 + labels.Sum(l => l.Length + 1) + 1 + 4];
        buf[2] = 0x01; // RD
        buf[5] = 0x01; // QDCOUNT
        int p = 12;
        foreach (var l in labels)
        {
            buf[p++] = (byte)l.Length;
            foreach (char c in l) buf[p++] = (byte)c;
        }
        buf[p++] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(p), type);
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(p + 2), 1);
        return buf;
    }

    private static (IPAddress[], int) ParseAnswer(byte[] d, ushort type)
    {
        int qd = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(4));
        int an = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(6));
        int p = 12;
        for (int i = 0; i < qd; i++) p = SkipName(d, p) + 4;

        var ips = new List<IPAddress>();
        int ttl = int.MaxValue;
        for (int i = 0; i < an && p < d.Length; i++)
        {
            p = SkipName(d, p);
            ushort rtype = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(p));
            int rttl = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(p + 4)), int.MaxValue);
            int len = BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(p + 8));
            p += 10;
            if (rtype == type && (len == 4 || len == 16))
            {
                ips.Add(new IPAddress(d.AsSpan(p, len)));
                ttl = Math.Min(ttl, rttl);
            }
            p += len;
        }
        return (ips.ToArray(), ttl);
    }

    private static int SkipName(byte[] d, int p)
    {
        while (p < d.Length)
        {
            byte len = d[p];
            if (len == 0) return p + 1;
            if ((len & 0xC0) == 0xC0) return p + 2; // compression pointer
            p += len + 1;
        }
        return p;
    }
}
