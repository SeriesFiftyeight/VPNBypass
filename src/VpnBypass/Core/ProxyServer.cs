using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VpnBypass.Core;

/// <summary>
/// Local HTTP proxy (CONNECT + plain HTTP) on 127.0.0.1. Resolves names with our own DoH
/// resolver, then sends each connection's first packet through <see cref="Desync"/>.
/// </summary>
public sealed class ProxyServer
{
    private const int MaxHead = 64 * 1024;
    private static readonly byte[] Established = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
    private static readonly byte[] BadGateway = Encoding.ASCII.GetBytes("HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private long _up, _down;
    private int _active;

    public int Port { get; private set; }
    public BypassProfile Profile { get; set; } = Profiles.All[0];
    public long BytesUp => Interlocked.Read(ref _up);
    public long BytesDown => Interlocked.Read(ref _down);
    public int ActiveConnections => Volatile.Read(ref _active);

    public event Action<LogLevel, string>? Log;

    public void Start(int preferredPort)
    {
        _cts = new CancellationTokenSource();
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, preferredPort);
            _listener.Start(1024);
        }
        catch (SocketException)
        {
            // Port taken (another app or a stale instance): let the OS pick one.
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start(1024);
        }
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _up = _down = 0;
        _ = AcceptLoopAsync(_listener, _cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Socket client;
            try { client = await listener.AcceptSocketAsync(ct); }
            catch { return; }
            _ = HandleAsync(client, ct);
        }
    }

    private async Task HandleAsync(Socket client, CancellationToken ct)
    {
        Interlocked.Increment(ref _active);
        Socket? remote = null;
        byte[] buf = ArrayPool<byte>.Shared.Rent(MaxHead);
        try
        {
            client.NoDelay = true;
            int len = await ReadHeadAsync(client, buf, ct);
            if (len <= 0) return;
            int headEnd = IndexOf(buf, len, "\r\n\r\n"u8) + 4;

            string head = Encoding.ASCII.GetString(buf, 0, headEnd);
            string[] line = head[..head.IndexOf("\r\n", StringComparison.Ordinal)].Split(' ');
            if (line.Length < 3) return;

            if (line[0].Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
            {
                if (!TrySplitHostPort(line[1], 443, out var host, out int port)) return;
                remote = await ConnectAsync(host, port, ct);
                if (remote == null) { await client.SendAsync(BadGateway, SocketFlags.None, ct); return; }
                await client.SendAsync(Established, SocketFlags.None, ct);

                // First client bytes are the TLS ClientHello: collect the whole record, then desync it.
                var first = buf.AsSpan(headEnd, len - headEnd).ToArray();
                first = await CompleteTlsRecordAsync(client, first, ct);
                if (first.Length > 0)
                {
                    await Desync.SendFirstAsync(remote, first, Profile, ct);
                    Interlocked.Add(ref _up, first.Length);
                }
            }
            else if (line[1].StartsWith("/proxy.pac", StringComparison.Ordinal))
            {
                // Windows fetches the PAC script from us directly (origin-form request).
                var body = Encoding.ASCII.GetBytes(SystemProxy.BuildPac(Port));
                var resp = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/x-ns-proxy-autoconfig\r\nContent-Length: {body.Length}\r\nCache-Control: no-cache\r\nConnection: close\r\n\r\n");
                await client.SendAsync(resp.Concat(body).ToArray(), SocketFlags.None, ct);
                return;
            }
            else
            {
                if (!Uri.TryCreate(line[1], UriKind.Absolute, out var uri) || uri.Scheme != "http") return;
                remote = await ConnectAsync(uri.IdnHost, uri.Port, ct);
                if (remote == null) { await client.SendAsync(BadGateway, SocketFlags.None, ct); return; }

                // Absolute-form -> origin-form, drop proxy-only headers.
                var sb = new StringBuilder($"{line[0]} {uri.PathAndQuery} {line[2]}\r\n");
                foreach (var h in head.Split("\r\n").Skip(1))
                    if (h.Length > 0 && !h.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase)) sb.Append(h).Append("\r\n");
                sb.Append("\r\n");
                var request = Encoding.ASCII.GetBytes(sb.ToString()).Concat(buf.AsSpan(headEnd, len - headEnd).ToArray()).ToArray();
                await Desync.SendFirstAsync(remote, request, Profile, ct);
                Interlocked.Add(ref _up, request.Length);
            }

            await PumpAsync(client, remote, ct);
        }
        catch
        {
            // Client/server went away; nothing to report per connection.
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
            remote?.Dispose();
            client.Dispose();
            Interlocked.Decrement(ref _active);
        }
    }

    private async Task<Socket?> ConnectAsync(string host, int port, CancellationToken ct)
    {
        var ips = await DohResolver.Instance.ResolveAsync(host, ct);
        foreach (var ip in NetworkState.Order(ips))
        {
            var s = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                t.CancelAfter(4000);
                await s.ConnectAsync(new IPEndPoint(ip, port), t.Token);
                return s;
            }
            catch
            {
                s.Dispose();
            }
        }
        Log?.Invoke(LogLevel.Warn,
            ips.Length == 0 ? $"dns :: no address for {host}"
            : !NetworkState.HasIpv4 && ips.All(i => i.AddressFamily == AddressFamily.InterNetwork) ? $"no IPv4 :: {host} is IPv4-only, unreachable on this link"
            : $"connect failed :: {host}:{port}");
        return null;
    }

    private static async Task<int> ReadHeadAsync(Socket s, byte[] buf, CancellationToken ct)
    {
        int len = 0;
        using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
        t.CancelAfter(10000);
        while (len < buf.Length)
        {
            int n = await s.ReceiveAsync(buf.AsMemory(len), SocketFlags.None, t.Token);
            if (n == 0) return -1;
            len += n;
            if (IndexOf(buf, len, "\r\n\r\n"u8) >= 0) return len;
        }
        return -1;
    }

    /// <summary>Keeps reading until the first TLS record is complete (ClientHellos can span reads).</summary>
    private static async Task<byte[]> CompleteTlsRecordAsync(Socket s, byte[] have, CancellationToken ct)
    {
        using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
        t.CancelAfter(3000);
        var data = new List<byte>(have);
        var chunk = new byte[16 * 1024];
        try
        {
            while (true)
            {
                if (data.Count >= 5 && data[0] == 0x16)
                {
                    int need = 5 + (data[3] << 8 | data[4]);
                    if (data.Count >= need) break;
                }
                else if (data.Count > 0) break; // Not TLS: send as-is.

                int n = await s.ReceiveAsync(chunk, SocketFlags.None, t.Token);
                if (n == 0) break;
                data.AddRange(chunk.AsSpan(0, n).ToArray());
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Client went quiet (e.g. server-speaks-first protocol); forward what we have.
        }
        return data.ToArray();
    }

    private async Task PumpAsync(Socket a, Socket b, CancellationToken ct)
    {
        using var done = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var up = CopyAsync(a, b, isUp: true, done.Token);
        var down = CopyAsync(b, a, isUp: false, done.Token);
        await Task.WhenAny(up, down);
        // One side closed: give the other a moment to flush, then tear down.
        await Task.WhenAny(Task.WhenAll(up, down), Task.Delay(5000, CancellationToken.None));
        done.Cancel();
    }

    private async Task CopyAsync(Socket from, Socket to, bool isUp, CancellationToken ct)
    {
        byte[] buf = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (true)
            {
                int n = await from.ReceiveAsync(buf, SocketFlags.None, ct);
                if (n == 0) break;
                int sent = 0;
                while (sent < n) sent += await to.SendAsync(buf.AsMemory(sent, n - sent), SocketFlags.None, ct);
                if (isUp) Interlocked.Add(ref _up, n); else Interlocked.Add(ref _down, n);
            }
            try { to.Shutdown(SocketShutdown.Send); } catch { }
        }
        catch { }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    private static bool TrySplitHostPort(string target, int defaultPort, out string host, out int port)
    {
        port = defaultPort;
        host = target;
        if (target.StartsWith('['))
        {
            int close = target.IndexOf(']');
            if (close < 0) return false;
            host = target[1..close];
            if (close + 1 < target.Length && target[close + 1] == ':') return int.TryParse(target[(close + 2)..], out port);
            return true;
        }
        int colon = target.LastIndexOf(':');
        if (colon > 0)
        {
            host = target[..colon];
            return int.TryParse(target[(colon + 1)..], out port);
        }
        return host.Length > 0;
    }

    private static int IndexOf(byte[] buf, int len, ReadOnlySpan<byte> needle) =>
        buf.AsSpan(0, len).IndexOf(needle);
}
