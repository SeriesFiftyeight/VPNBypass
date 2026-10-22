using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using VpnBypass.Core;

namespace VpnBypass;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public static class QualityBrushes
{
    public static Brush For(PingQuality q) => (Brush)Application.Current.Resources[q switch
    {
        PingQuality.Good => "Neon",
        PingQuality.Warn => "Amber",
        _ => "Red"
    }];

    public static string LabelKey(PingQuality q) => q switch
    {
        PingQuality.Good => "PingGood",
        PingQuality.Warn => "PingWarn",
        PingQuality.Bad => "PingBad",
        _ => "PingDown"
    };
}

public sealed class PingRow : Observable
{
    public const int HistoryLength = 40;
    private readonly Queue<double?> _history = new();

    public PingRow(PingTarget target) { Name = target.Name; Host = target.Host; }

    public string Name { get; }
    public string Host { get; }

    private string _msText = "···";
    public string MsText { get => _msText; private set => Set(ref _msText, value); }

    private Brush _brush = Brushes.Gray;
    public Brush Brush { get => _brush; private set => Set(ref _brush, value); }

    private string _label = "";
    public string Label { get => _label; private set => Set(ref _label, value); }

    private double _bar;
    /// <summary>0..1 signal strength (inverse of latency against the critical limit).</summary>
    public double Bar { get => _bar; private set => Set(ref _bar, value); }

    private double?[] _snapshot = [];
    public double?[] History { get => _snapshot; private set => Set(ref _snapshot, value); }

    private PingQuality _quality = PingQuality.Down;

    public void RefreshLabel() => Label = Loc.T(QualityBrushes.LabelKey(_quality));

    public void Push(PingSample s, AppSettings cfg)
    {
        _quality = s.Quality;
        _history.Enqueue(s.Ms);
        while (_history.Count > HistoryLength) _history.Dequeue();
        History = _history.ToArray();

        MsText = s.Ms is long ms ? $"{ms} ms" : "TIMEOUT";
        Brush = QualityBrushes.For(s.Quality);
        Label = Loc.T(QualityBrushes.LabelKey(s.Quality));
        Bar = s.Ms is long v ? Math.Clamp(1 - v / (cfg.PingCriticalMs * 1.6), 0.06, 1) : 0;
    }
}

public sealed class LogLine
{
    public required string Time { get; init; }
    public required string Tag { get; init; }
    public required string Text { get; init; }
    public required Brush Brush { get; init; }
}
