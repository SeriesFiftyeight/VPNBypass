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
    public int PresetId { get; set; } = 1;
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
        new() { Name = "Cloudflare", Host = "1.1.1.1" },
        new() { Name = "Google", Host = "8.8.8.8" },
        new() { Name = "YouTube", Host = "youtube.com" },
        new() { Name = "Yandex DNS", Host = "77.88.8.8" }
    ];

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
                    s.Normalize();
                    return s;
                }
            }
        }
        catch
        {
            // Corrupt file: fall back to defaults.
        }
        return new AppSettings();
    }

    public void Normalize()
    {
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
