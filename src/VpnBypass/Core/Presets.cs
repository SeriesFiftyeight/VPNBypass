namespace VpnBypass.Core;

public sealed class BypassPreset
{
    public required int Id { get; init; }
    public required string Code { get; init; }
    public required string NameTr { get; init; }
    public required string NameEn { get; init; }
    public required string DescTr { get; init; }
    public required string DescEn { get; init; }
    public required string Arguments { get; init; }
    public bool Recommended { get; init; }

    public string Name => Loc.IsTurkish ? NameTr : NameEn;
    public string Description => Loc.IsTurkish ? DescTr : DescEn;
}

public static class Presets
{
    // Secure DNS redirection (Yandex, non-standard port) used by several modes.
    private const string YandexDns = "--dns-addr 77.88.8.8 --dns-port 1253 --dnsv6-addr 2a02:6b8::feed:0ff --dnsv6-port 1253";

    // --max-payload skips DPI processing on large packets (downloads, streams), keeping CPU and latency low.
    private const string Fast = "--max-payload 1200";

    public static IReadOnlyList<BypassPreset> All { get; } =
    [
        new()
        {
            Id = 1, Code = "STD-TR", Recommended = true,
            NameTr = "Standart TR", NameEn = "Standard TR",
            DescTr = "Türk Telekom, TurkNet, Kablonet, Vodafone. TTL 5 + güvenli DNS.",
            DescEn = "Turk Telekom, TurkNet, Kablonet, Vodafone. TTL 5 + secure DNS.",
            Arguments = $"-5 --set-ttl 5 {YandexDns}"
        },
        new()
        {
            Id = 2, Code = "SOL-4", Recommended = true,
            NameTr = "Superonline Mod 4", NameEn = "Superonline Mode 4",
            DescTr = "Superonline fiber için en başarılı mod.",
            DescEn = "Most effective mode for Superonline fiber.",
            Arguments = $"-5 {YandexDns}"
        },
        new()
        {
            Id = 3, Code = "SOL-1",
            NameTr = "Superonline Mod 1 · TTL 3", NameEn = "Superonline Mode 1 · TTL 3",
            DescTr = "Saf TTL 3 paket manipülasyonu.",
            DescEn = "Pure TTL 3 packet manipulation.",
            Arguments = $"--set-ttl 3 {Fast}"
        },
        new()
        {
            Id = 4, Code = "SOL-2",
            NameTr = "Superonline Mod 2 · -5", NameEn = "Superonline Mode 2 · -5",
            DescTr = "Saf -5 parçalama modu, DNS yönlendirmesi yok.",
            DescEn = "Pure -5 fragmentation, no DNS redirection.",
            Arguments = "-5"
        },
        new()
        {
            Id = 5, Code = "SOL-3",
            NameTr = "Superonline Mod 3 · TTL 3 + DNS", NameEn = "Superonline Mode 3 · TTL 3 + DNS",
            DescTr = "TTL 3 ve güvenli DNS birlikte.",
            DescEn = "TTL 3 combined with secure DNS.",
            Arguments = $"--set-ttl 3 {Fast} {YandexDns}"
        },
        new()
        {
            Id = 6, Code = "MAX-9",
            NameTr = "Agresif · Maksimum (-9)", NameEn = "Aggressive · Maximum (-9)",
            DescTr = "Zorlu filtreler için derin parçalama + sahte istek.",
            DescEn = "Deep fragmentation + fake requests for tough filters.",
            Arguments = $"-9 {YandexDns}"
        },
        new()
        {
            Id = 7, Code = "CF-DNS",
            NameTr = "Cloudflare DNS · TTL 5", NameEn = "Cloudflare DNS · TTL 5",
            DescTr = "Standart bypass, Cloudflare 1.1.1.1 DNS ile.",
            DescEn = "Standard bypass with Cloudflare 1.1.1.1 DNS.",
            Arguments = "-5 --set-ttl 5 --dns-addr 1.1.1.1 --dns-port 53"
        },
        new()
        {
            Id = 8, Code = "TURBO",
            NameTr = "Turbo · Saf Parçalama", NameEn = "Turbo · Pure Splitting",
            DescTr = "DNS'e dokunmaz, en düşük gecikme için sadece paket böler.",
            DescEn = "Leaves DNS alone, only splits packets for the lowest latency.",
            Arguments = $"-5 -e 1 -q --reverse-frag --set-ttl 5 {Fast}"
        }
    ];

    public static BypassPreset Get(int id) => All.FirstOrDefault(p => p.Id == id) ?? All[0];
}
