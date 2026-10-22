using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace VpnBypass.Controls;

/// <summary>Low-cost digital rain backdrop: cached glyphs, ~16 fps, paused while hidden.</summary>
public sealed class MatrixRain : FrameworkElement
{
    private const string Glyphs = "01アイウエオカキクケコサシスセソタチツテトナニヌネノ<>/\\{}[]#$%&*+=VPNBYPASS";
    private const double Cell = 16;
    private const int Trail = 14;

    private readonly Random _rng = new();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(60) };
    private readonly Dictionary<(char, int), FormattedText> _cache = new();
    private double[] _heads = [];
    private double[] _speeds = [];
    private char[][] _cols = [];

    public MatrixRain()
    {
        IsHitTestVisible = false;
        _timer.Tick += (_, _) => { Step(); InvalidateVisual(); };
        IsVisibleChanged += (_, _) => UpdateTimer();
        Loaded += (_, _) => UpdateTimer();
        Unloaded += (_, _) => _timer.Stop();
        SizeChanged += (_, _) => Reset();
    }

    private void UpdateTimer()
    {
        if (IsVisible && IsLoaded) _timer.Start(); else _timer.Stop();
    }

    private void Reset()
    {
        int n = Math.Max(1, (int)(ActualWidth / Cell));
        int rows = Math.Max(1, (int)(ActualHeight / Cell) + Trail);
        _heads = new double[n];
        _speeds = new double[n];
        _cols = new char[n][];
        for (int i = 0; i < n; i++)
        {
            _heads[i] = _rng.Next(-rows, rows);
            _speeds[i] = 0.25 + _rng.NextDouble() * 0.6;
            _cols[i] = Enumerable.Range(0, rows).Select(_ => RandomGlyph()).ToArray();
        }
    }

    private char RandomGlyph() => Glyphs[_rng.Next(Glyphs.Length)];

    private void Step()
    {
        int rows = (int)(ActualHeight / Cell) + Trail;
        for (int i = 0; i < _heads.Length; i++)
        {
            _heads[i] += _speeds[i];
            if (_heads[i] - Trail > rows) { _heads[i] = -_rng.Next(0, rows / 2 + 1); _speeds[i] = 0.25 + _rng.NextDouble() * 0.6; }
            if (_rng.NextDouble() < 0.08) _cols[i][_rng.Next(_cols[i].Length)] = RandomGlyph();
        }
    }

    private FormattedText Glyph(char c, int level)
    {
        if (_cache.TryGetValue((c, level), out var ft)) return ft;
        byte a = (byte)(level == 0 ? 255 : 230 * (Trail - level) / Trail);
        var brush = new SolidColorBrush(level == 0 ? Color.FromArgb(255, 200, 255, 230) : Color.FromArgb(a, 0, 255, 156));
        brush.Freeze();
        ft = new FormattedText(c.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Cascadia Mono"), 13, brush, 1.0);
        _cache[(c, level)] = ft;
        return ft;
    }

    protected override void OnRender(DrawingContext dc)
    {
        for (int i = 0; i < _heads.Length; i++)
        {
            int head = (int)_heads[i];
            var col = _cols[i];
            for (int k = 0; k < Trail; k++)
            {
                int row = head - k;
                if (row < 0 || row >= col.Length) continue;
                double y = row * Cell;
                if (y > ActualHeight) continue;
                dc.DrawText(Glyph(col[row], k), new Point(i * Cell, y));
            }
        }
    }
}
