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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _plainHosts = new(StringComparer.OrdinalIgnoreCase);

    public int Port { get; private set; }
    public BypassProfile Profile
    {
        get => _profile;
        set { _profile = value; _plainHosts.Clear(); }
    }
    private BypassProfile _profile = Profiles.All[0];
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
        Task? prelude = null;
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
                    (remote, prelude) = await SendHelloAsync(client, remote, host, port, first, ct);
                    if (remote == null) return;
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

            await PumpAsync(client, remote, prelude, ct);
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

    /// <summary>
    /// Sends the ClientHello desynced. Some strict servers (e.g. turkiye.gov.tr, banks) reject a
    /// fragmented ClientHello by closing or alerting before saying anything. Those are not
    /// DPI-blocked, so we reconnect and send the hello untouched, and remember the host.
    /// Returns the socket to keep pumping on, or null if both attempts failed.
    /// </summary>
    private async Task<(Socket? Remote, Task? Prelude)> SendHelloAsync(Socket client, Socket remote, string host, int port, byte[] hello, CancellationToken ct)
    {
        bool isTls = hello.Length > 5 && hello[0] == 0x16;
        if (!isTls || _plainHosts.ContainsKey(host))
        {
            await SendAllAsync(remote, hello, ct);
            return (remote, null);
        }

        await Desync.SendFirstAsync(remote, hello, Profile, ct);

        var reply = new byte[16 * 1024];
        var read = remote.ReceiveAsync(reply, SocketFlags.None, ct).AsTask();
        await Task.WhenAny(read, Task.Delay(3000, ct));

        // No reply at all: some servers silently drop a split hello. Treat like a rejection.
        int n = !read.IsCompleted ? -1 : read.IsCompletedSuccessfully ? read.Result : -1;
        if (n > 0 && reply[0] != 0x15)
        {
            // Server answered normally: hand its first bytes to the client and carry on.
            await SendAllAsync(client, reply.AsMemory(0, n), ct);
            Interlocked.Add(ref _down, n);
            return (remote, null);
        }

        // Closed, reset, TLS alert or silence after our desynced hello: retry plain.
        remote.Dispose();
        try { await read; } catch { } // let the aborted receive finish before reusing its buffer
        var retry = await ConnectAsync(host, port, ct);
        if (retry == null) return (null, null);
        await SendAllAsync(retry, hello, ct);

        reply = new byte[16 * 1024];
        var retryRead = retry.ReceiveAsync(reply, SocketFlags.None, ct).AsTask();
        await Task.WhenAny(retryRead, Task.Delay(6000, ct));
        if (!retryRead.IsCompleted) return (retry, ForwardLaterAsync(retryRead, reply, client, ct));
        int m = retryRead.IsCompletedSuccessfully ? retryRead.Result : -1;
        if (m <= 0) { retry.Dispose(); return (null, null); }

        await SendAllAsync(client, reply.AsMemory(0, m), ct);
        Interlocked.Add(ref _down, m);
        if (reply[0] != 0x15)
        {
            // Only remember hosts that really work unmodified (never a DPI-blocked one).
            _plainHosts[host] = 0;
            Log?.Invoke(LogLevel.Info, $"plain :: {host} rejects split hello, sent unmodified");
        }
        return (retry, null);
    }

    private async Task ForwardLaterAsync(Task<int> read, byte[] reply, Socket client, CancellationToken ct)
    {
        int n = await read;
        if (n <= 0) return;
        await SendAllAsync(client, reply.AsMemory(0, n), ct);
        Interlocked.Add(ref _down, n);
    }

    private static async Task SendAllAsync(Socket s, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        int sent = 0;
        while (sent < data.Length) sent += await s.SendAsync(data[sent..], SocketFlags.None, ct);
    }

    private async Task<Socket?> ConnectAsync(string host, int port, CancellationToken ct)
    {
        var ips = await DohResolver.Instance.ResolveAsync(host, ct);
        var winner = await HappyEyeballsAsync(Interleave(NetworkState.Order(ips)), port, ct);
        if (winner != null) return winner;
        Log?.Invoke(LogLevel.Warn,
            ips.Length == 0 ? $"dns :: no address for {host}"
            : !NetworkState.HasIpv4 && ips.All(i => i.AddressFamily == AddressFamily.InterNetwork) ? $"no IPv4 :: {host} is IPv4-only, unreachable on this link"
            : $"connect failed :: {host}:{port}");
        return null;
    }

    /// <summary>Alternates address families (preferred family first) per RFC 8305.</summary>
    private static List<IPAddress> Interleave(IEnumerable<IPAddress> ordered)
    {
        var list = ordered.ToList();
        if (list.Count == 0) return list;
        var first = list.Where(a => a.AddressFamily == list[0].AddressFamily).ToList();
        var other = list.Where(a => a.AddressFamily != list[0].AddressFamily).ToList();
        var result = new List<IPAddress>(list.Count);
        for (int i = 0; i < Math.Max(first.Count, other.Count); i++)
        {
            if (i < first.Count) result.Add(first[i]);
            if (i < other.Count) result.Add(other[i]);
        }
        return result;
    }

    /// <summary>
    /// Happy Eyeballs: start an attempt, and if it hasn't connected in 250 ms start the next one in
    /// parallel. The first socket to connect wins; the rest are closed. Gives up after 8 s.
    /// </summary>
    private static async Task<Socket?> HappyEyeballsAsync(List<IPAddress> addresses, int port, CancellationToken ct)
    {
        if (addresses.Count == 0) return null;
        using var all = CancellationTokenSource.CreateLinkedTokenSource(ct);
        all.CancelAfter(8000);
        var attempts = new List<Task<Socket?>>();

        async Task<Socket?> Attempt(IPAddress ip)
        {
            var s = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await s.ConnectAsync(new IPEndPoint(ip, port), all.Token);
                return s;
            }
            catch
            {
                s.Dispose();
                return null;
            }
        }

        Socket? winner = null;
        int next = 0;
        while (winner == null && !all.IsCancellationRequested)
        {
            if (next < addresses.Count) attempts.Add(Attempt(addresses[next++]));
            var pending = attempts.Where(t => !t.IsCompleted).ToList();
            if (pending.Count == 0)
            {
                if (next >= addresses.Count) break;
                continue; // everything so far failed fast: try the next address right away
            }

            var stagger = next < addresses.Count ? Task.Delay(250, all.Token) : Task.Delay(Timeout.Infinite, all.Token);
            var done = await Task.WhenAny(pending.Cast<Task>().Append(stagger));
            winner = attempts.FirstOrDefault(t => t.IsCompletedSuccessfully && t.Result != null)?.Result;
            if (done == stagger && stagger.IsCanceled) break;
        }

        all.Cancel();
        foreach (var t in attempts)
        {
            try
            {
                var s = await t;
                if (s != null && s != winner) s.Dispose();
            }
            catch { }
        }
        return winner;
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

    private async Task PumpAsync(Socket a, Socket b, Task? downPrelude, CancellationToken ct)
    {
        using var done = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var up = CopyAsync(a, b, isUp: true, done.Token);
        var down = downPrelude == null
            ? CopyAsync(b, a, isUp: false, done.Token)
            : DownAfterAsync(downPrelude, b, a, done.Token);
        await Task.WhenAny(up, down);
        // One side closed: give the other a moment to flush, then tear down.
        await Task.WhenAny(Task.WhenAll(up, down), Task.Delay(5000, CancellationToken.None));
        done.Cancel();
    }

    private async Task DownAfterAsync(Task prelude, Socket from, Socket to, CancellationToken ct)
    {
        try { await prelude; } catch { return; }
        await CopyAsync(from, to, isUp: false, ct);
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
