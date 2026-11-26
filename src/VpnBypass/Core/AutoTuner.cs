using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace VpnBypass.Core;

/// <summary>Finds the fastest profile that gets a TLS handshake through to commonly blocked sites.</summary>
public static class AutoTuner
{
    public static readonly string[] TestHosts = ["discord.com", "www.roblox.com", "www.wattpad.com"];

    public sealed record Result(BypassProfile? Profile, int Passed, int Total, long AvgMs);

    /// <summary>Tests "no bypass" first (Profile = null), then every profile.</summary>
    public static async Task<List<Result>> RunAsync(Action<Result> progress, CancellationToken ct)
    {
        var results = new List<Result>();
        foreach (var profile in new BypassProfile?[] { null }.Concat(Profiles.All))
        {
            var times = await Task.WhenAll(TestHosts.Select(h => HandshakeAsync(h, profile, ct)));
            var ok = times.Where(t => t.HasValue).Select(t => t!.Value).ToList();
            var r = new Result(profile, ok.Count, TestHosts.Length, ok.Count > 0 ? (long)ok.Average() : -1);
            results.Add(r);
            progress(r);
        }
        return results;
    }

    /// <summary>Best profile: most sites passed, then lowest handshake time; ties go to the lighter (earlier) profile.</summary>
    public static Result? Pick(IEnumerable<Result> results) => results
        .Where(r => r.Profile != null && r.Passed > 0)
        .OrderByDescending(r => r.Passed)
        .ThenBy(r => r.Profile!.Disorder) // disorder costs a retransmit per connection
        .ThenBy(r => r.AvgMs)
        .FirstOrDefault();

    private static async Task<long?> HandshakeAsync(string host, BypassProfile? profile, CancellationToken ct)
    {
        try
        {
            var ips = await DohResolver.Instance.ResolveAsync(host, ct);
            foreach (var ip in NetworkState.Order(ips).Take(2))
            {
                using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                t.CancelAfter(5000);
                using var sock = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                var sw = Stopwatch.StartNew();
                try
                {
                    await sock.ConnectAsync(new IPEndPoint(ip, 443), t.Token);
                    await using var ssl = new SslStream(new DesyncStream(sock, profile), leaveInnerStreamOpen: false);
                    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, t.Token);
                    return sw.ElapsedMilliseconds;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch { /* reset or timeout: try next address */ }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { }
        return null;
    }

    /// <summary>Network stream whose first write (the ClientHello) goes through <see cref="Desync"/>.</summary>
    private sealed class DesyncStream(Socket socket, BypassProfile? profile) : NetworkStream(socket, ownsSocket: false)
    {
        private bool _first = true;

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            if (_first && profile != null)
            {
                _first = false;
                await Desync.SendFirstAsync(Socket, buffer.ToArray(), profile, ct);
                return;
            }
            _first = false;
            await base.WriteAsync(buffer, ct);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();
    }
}
