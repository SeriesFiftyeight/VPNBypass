using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace VpnBypass.Core;

/// <summary>NoIpv4: target is an IPv4 address but this machine has no usable IPv4 address.</summary>
public enum PingQuality { Good, Warn, Bad, Down, NoIpv4 }

public sealed class PingSample
{
    public required PingTarget Target { get; init; }
    public long? Ms { get; init; }
    public PingQuality Quality { get; init; }
}

/// <summary>Pings all targets in parallel on a fixed interval; ICMP first, TCP:443 handshake as fallback.</summary>
public sealed class PingMonitor : IDisposable
{
    private CancellationTokenSource? _cts;

    /// <summary>Fires on a thread-pool thread after every round. The flag says whether IPv4 is usable.</summary>
    public event Action<IReadOnlyList<PingSample>, bool>? Round;

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        _ = LoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var s = AppSettings.Current;
            var targets = s.PingTargets.ToList();
            try
            {
                bool ipv4 = NetworkState.HasIpv4;
                var addresses = await Task.WhenAll(targets.Select(t => DohResolver.Instance.ResolveAsync(t.Host, ct)));

                // Without IPv4, a target that only has IPv4 addresses can't be reached at all: that's
                // a local network problem, not packet loss.
                bool[] skip = addresses.Select(a => !ipv4 && a.Length > 0 && a.All(x => x.AddressFamily == AddressFamily.InterNetwork)).ToArray();

                var results = await Task.WhenAll(targets.Select((_, i) => skip[i] ? Task.FromResult<long?>(null) : MeasureAsync(addresses[i], ct)));
                var samples = targets.Select((t, i) => new PingSample
                {
                    Target = t,
                    Ms = results[i],
                    Quality = skip[i] ? PingQuality.NoIpv4 : Classify(results[i], s)
                }).ToList();
                Round?.Invoke(samples, ipv4);
                await Task.Delay(TimeSpan.FromSeconds(s.PingIntervalSec), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                await Task.Delay(1000, CancellationToken.None);
            }
        }
    }

    public static PingQuality Classify(long? ms, AppSettings s) => ms switch
    {
        null => PingQuality.Down,
        var v when v <= s.PingGoodMs => PingQuality.Good,
        var v when v <= s.PingCriticalMs => PingQuality.Warn,
        _ => PingQuality.Bad
    };

    /// <param name="addresses">Resolved with our own DoH, so poisoned ISP DNS can't send the ping to a block page.</param>
    public static async Task<long?> MeasureAsync(IPAddress[] addresses, CancellationToken ct = default)
    {
        var ip = NetworkState.Order(addresses).FirstOrDefault();
        if (ip == null) return null;

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ip, TimeSpan.FromMilliseconds(1500), cancellationToken: ct);
            if (reply.Status == IPStatus.Success) return Math.Max(1, reply.RoundtripTime);
        }
        catch (OperationCanceledException) { throw; }
        catch { }

        // Some hosts drop ICMP; time a TCP handshake instead.
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(1500);
            using var sock = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            var sw = Stopwatch.StartNew();
            await sock.ConnectAsync(new IPEndPoint(ip, 443), timeout.Token);
            return Math.Max(1, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    public void Dispose() => Stop();
}
