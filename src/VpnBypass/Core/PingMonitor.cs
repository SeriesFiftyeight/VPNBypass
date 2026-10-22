using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace VpnBypass.Core;

public enum PingQuality { Good, Warn, Bad, Down }

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

    /// <summary>Fires on a thread-pool thread after every round.</summary>
    public event Action<IReadOnlyList<PingSample>>? Round;

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
                var results = await Task.WhenAll(targets.Select(t => MeasureAsync(t.Host, ct)));
                var samples = targets.Select((t, i) => new PingSample
                {
                    Target = t,
                    Ms = results[i],
                    Quality = Classify(results[i], s)
                }).ToList();
                Round?.Invoke(samples);
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

    public static async Task<long?> MeasureAsync(string host, CancellationToken ct = default)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, TimeSpan.FromMilliseconds(1500), cancellationToken: ct);
            if (reply.Status == IPStatus.Success) return Math.Max(1, reply.RoundtripTime);
        }
        catch (OperationCanceledException) { throw; }
        catch { }

        // Some hosts drop ICMP; time a TCP handshake instead.
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(1500);
            using var sock = new Socket(SocketType.Stream, ProtocolType.Tcp);
            var sw = Stopwatch.StartNew();
            await sock.ConnectAsync(host, 443, timeout.Token);
            return Math.Max(1, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    public void Dispose() => Stop();
}
