namespace VpnBypass.Core;

/// <summary>VPN Bypass's own engine: local desync proxy + system proxy switch. Events may fire on any thread.</summary>
public sealed class BypassEngine
{
    public static BypassEngine Instance { get; } = new();

    private readonly ProxyServer _proxy = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsRunning { get; private set; }
    public BypassProfile ActiveProfile { get; private set; } = Profiles.Get(AppSettings.Current.ProfileId);
    public DateTime? StartedAt { get; private set; }
    public long BytesTotal => _proxy.BytesUp + _proxy.BytesDown;
    public int ActiveConnections => _proxy.ActiveConnections;

    public event Action<bool>? StatusChanged;
    public event Action<LogLevel, string>? Log;

    private BypassEngine()
    {
        _proxy.Log += (l, m) => Log?.Invoke(l, m);
    }

    public async Task<bool> StartAsync(BypassProfile? profile = null)
    {
        await _gate.WaitAsync();
        try
        {
            ActiveProfile = profile ?? Profiles.Get(AppSettings.Current.ProfileId);
            _proxy.Profile = ActiveProfile;
            if (IsRunning) return true;

            try
            {
                _proxy.Start(AppSettings.Current.ProxyPort);
                SystemProxy.Enable(_proxy.Port);
            }
            catch (Exception ex)
            {
                _proxy.Stop();
                SystemProxy.Restore();
                Log?.Invoke(LogLevel.Error, $"engine start failed :: {ex.Message}");
                return false;
            }

            IsRunning = true;
            StartedAt = DateTime.Now;
            var dns = DohResolver.Get(AppSettings.Current.DnsProvider);
            Log?.Invoke(LogLevel.Info, $"dns :: {dns.Name} DoH ({dns.Host})");
            Log?.Invoke(LogLevel.Ok, $"tunnel ACTIVE :: 127.0.0.1:{_proxy.Port} :: [{ActiveProfile.Code}] {ActiveProfile.Recipe}");
            StatusChanged?.Invoke(true);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Switches the desync recipe live; open connections keep theirs, new ones use the new one.</summary>
    public void SetProfile(BypassProfile profile)
    {
        ActiveProfile = profile;
        _proxy.Profile = profile;
        if (IsRunning) Log?.Invoke(LogLevel.Info, $"profile live-switched -> [{profile.Code}] {profile.Recipe}");
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            bool was = IsRunning;
            StopNow();
            if (was) Log?.Invoke(LogLevel.Info, "tunnel closed, system proxy restored");
            StatusChanged?.Invoke(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Synchronous stop for shutdown paths. Always restores the user's proxy settings.</summary>
    public void StopNow()
    {
        SystemProxy.Restore();
        _proxy.Stop();
        IsRunning = false;
        StartedAt = null;
    }
}
