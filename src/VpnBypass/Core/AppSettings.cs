using System.IO;
using System.Text.Json;

namespace VpnBypass.Core;

public sealed class PingTarget
{
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
}

public sealed class AppSettings
{
    /// <summary>Bumped when defaults change in a way old files should pick up.</summary>
    public int SettingsVersion { get; set; }
    public int ProfileId { get; set; } = 1;
    public string DnsProvider { get; set; } = "cloudflare";
    public int ProxyPort { get; set; } = 8881;
    public bool AutoConnect { get; set; } = true;
    public bool StartInTray { get; set; }
    public bool CloseToTray { get; set; } = true;
    public string Language { get; set; } = "TR";

    // Ping limits: <= Good is green, <= Critical is amber, above is red.
    public int PingGoodMs { get; set; } = 60;
    public int PingCriticalMs { get; set; } = 150;
    public int PingIntervalSec { get; set; } = 2;
    public bool AlertOnHighPing { get; set; } = true;

    public List<PingTarget> PingTargets { get; set; } =
    [
        new() { Name = "Cloudflare", Host = "one.one.one.one" },
        new() { Name = "Google", Host = "google.com" },
        new() { Name = "YouTube", Host = "youtube.com" },
        new() { Name = "Discord", Host = "discord.com" }
    ];

    private const int CurrentVersion = 3;
    private static readonly string[] V2DefaultHosts = ["1.1.1.1", "8.8.8.8", "youtube.com", "77.88.8.8"];

    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "vpnbypass.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Current { get; private set; } = Load();

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (s != null)
                {
                    s.Migrate();
                    s.Normalize();
                    return s;
                }
            }
        }
        catch
        {
            // Corrupt file: fall back to defaults.
        }
        return new AppSettings { SettingsVersion = CurrentVersion };
    }

    private void Migrate()
    {
        if (SettingsVersion >= CurrentVersion) return;
        // v2 defaults were IPv4 literals that fail on IPv6-only links; move untouched lists to hostnames.
        if (PingTargets.Select(x => x.Host).SequenceEqual(V2DefaultHosts))
            PingTargets = new AppSettings().PingTargets;
        SettingsVersion = CurrentVersion;
    }

    public void Normalize()
    {
        if (!Profiles.All.Any(p => p.Id == ProfileId)) ProfileId = 1;
        if (!DohResolver.Providers.Any(p => p.Id == DnsProvider)) DnsProvider = "cloudflare";
        ProxyPort = Math.Clamp(ProxyPort, 1024, 65535);
        PingGoodMs = Math.Clamp(PingGoodMs, 5, 2000);
        PingCriticalMs = Math.Clamp(PingCriticalMs, PingGoodMs + 1, 5000);
        PingIntervalSec = Math.Clamp(PingIntervalSec, 1, 60);
        PingTargets = PingTargets.Where(t => !string.IsNullOrWhiteSpace(t.Host)).Take(8).ToList();
    }

    public void Save()
    {
        try
        {
            Normalize();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch
        {
            // Read-only location; settings stay in memory for this session.
        }
    }
}
