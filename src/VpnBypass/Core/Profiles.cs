namespace VpnBypass.Core;

/// <summary>A desync recipe applied to the first packet (TLS ClientHello / HTTP request) of each connection.</summary>
public sealed class BypassProfile
{
    public required int Id { get; init; }
    public required string Code { get; init; }
    public required string NameTr { get; init; }
    public required string NameEn { get; init; }
    public required string DescTr { get; init; }
    public required string DescEn { get; init; }

    /// <summary>Split the ClientHello into two TLS records inside the SNI hostname.</summary>
    public bool TlsRecordSplit { get; init; }
    /// <summary>Send the first byte as its own TCP segment.</summary>
    public bool SplitFirstByte { get; init; }
    /// <summary>Cut TCP segments inside the SNI hostname.</summary>
    public bool SplitAtSni { get; init; }
    /// <summary>Send the first segment with TTL 1 so it dies before the server; the OS retransmits it, so DPI sees data out of order.</summary>
    public bool Disorder { get; init; }

    public string Name => Loc.IsTurkish ? NameTr : NameEn;
    public string Description => Loc.IsTurkish ? DescTr : DescEn;

    public string Recipe => string.Join(" + ", new[]
    {
        TlsRecordSplit ? "tlsrec@sni" : null,
        SplitFirstByte ? "split@1" : null,
        SplitAtSni ? "split@sni" : null,
        Disorder ? "disorder" : null
    }.Where(s => s != null));
}

public static class Profiles
{
    public static IReadOnlyList<BypassProfile> All { get; } =
    [
        new()
        {
            Id = 1, Code = "STD",
            NameTr = "Standart", NameEn = "Standard",
            DescTr = "Türk Telekom, TurkNet, Vodafone, Kablonet. Ek gecikme yok.",
            DescEn = "Turk Telekom, TurkNet, Vodafone, Kablonet. No added latency.",
            TlsRecordSplit = true, SplitAtSni = true
        },
        new()
        {
            Id = 2, Code = "DSR",
            NameTr = "Sıra Bozma", NameEn = "Disorder",
            DescTr = "Superonline ve sıkı filtreler. Yeni bağlantı başına kısa bir gecikme ekler.",
            DescEn = "Superonline and strict filters. Adds a short delay per new connection.",
            TlsRecordSplit = true, SplitFirstByte = true, Disorder = true
        },
        new()
        {
            Id = 3, Code = "MAX",
            NameTr = "Agresif", NameEn = "Aggressive",
            DescTr = "Bütün teknikler birlikte. Diğerleri çalışmazsa dene.",
            DescEn = "Every technique at once. Try this when others fail.",
            TlsRecordSplit = true, SplitFirstByte = true, SplitAtSni = true, Disorder = true
        },
        new()
        {
            Id = 4, Code = "TLS",
            NameTr = "TLS Bölme", NameEn = "TLS Split",
            DescTr = "Sadece TLS kaydını böler. En hafif yöntem.",
            DescEn = "Only splits the TLS record. Lightest method.",
            TlsRecordSplit = true
        },
        new()
        {
            Id = 5, Code = "TCP",
            NameTr = "TCP Bölme", NameEn = "TCP Split",
            DescTr = "Sadece TCP paketini site adının ortasından böler.",
            DescEn = "Only cuts the TCP segment inside the hostname.",
            SplitAtSni = true
        }
    ];

    public static BypassProfile Get(int id) => All.FirstOrDefault(p => p.Id == id) ?? All[0];
}
