using System.Windows;
using System.Windows.Media;

namespace VpnBypass.Controls;

/// <summary>Latency history line with dashed good/critical limit guides. Null values are gaps (timeouts).</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double?>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.Lime, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GoodLimitProperty = DependencyProperty.Register(
        nameof(GoodLimit), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(60.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CriticalLimitProperty = DependencyProperty.Register(
        nameof(CriticalLimit), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(150.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowFillProperty = DependencyProperty.Register(
        nameof(ShowFill), typeof(bool), typeof(Sparkline),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double?>? Values { get => (IReadOnlyList<double?>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double GoodLimit { get => (double)GetValue(GoodLimitProperty); set => SetValue(GoodLimitProperty, value); }
    public double CriticalLimit { get => (double)GetValue(CriticalLimitProperty); set => SetValue(CriticalLimitProperty, value); }
    public bool ShowFill { get => (bool)GetValue(ShowFillProperty); set => SetValue(ShowFillProperty, value); }

    private static readonly Pen GoodPen = MakeGuide(Color.FromArgb(90, 0, 255, 156));
    private static readonly Pen CritPen = MakeGuide(Color.FromArgb(110, 255, 59, 92));

    private static Pen MakeGuide(Color c)
    {
        var p = new Pen(new SolidColorBrush(c), 1) { DashStyle = new DashStyle([3, 4], 0) };
        p.Freeze();
        return p;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        var vals = Values;
        if (vals is not { Count: > 1 } || w <= 0 || h <= 0) return;

        double max = Math.Max(CriticalLimit * 1.25, vals.Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty(0).Max() * 1.1);
        double Y(double v) => h - 2 - Math.Min(v, max) / max * (h - 4);
        double step = w / (vals.Count - 1);

        dc.DrawLine(GoodPen, new Point(0, Y(GoodLimit)), new Point(w, Y(GoodLimit)));
        dc.DrawLine(CritPen, new Point(0, Y(CriticalLimit)), new Point(w, Y(CriticalLimit)));

        var pen = new Pen(Stroke, 1.6) { LineJoin = PenLineJoin.Round };
        var geo = new StreamGeometry();
        var fill = new StreamGeometry();
        using (var g = geo.Open())
        using (var f = fill.Open())
        {
            bool open = false;
            Point start = default, last = default;
            for (int i = 0; i < vals.Count; i++)
            {
                if (vals[i] is not double v)
                {
                    if (open) { f.LineTo(new Point(last.X, h), false, false); f.LineTo(new Point(start.X, h), false, false); }
                    open = false;
                    continue;
                }
                var pt = new Point(i * step, Y(v));
                if (!open) { g.BeginFigure(pt, false, false); f.BeginFigure(pt, true, true); start = pt; open = true; }
                else { g.LineTo(pt, true, true); f.LineTo(pt, false, false); }
                last = pt;
            }
            if (open) { f.LineTo(new Point(last.X, h), false, false); f.LineTo(new Point(start.X, h), false, false); }
        }

        if (ShowFill && Stroke is SolidColorBrush sb)
        {
            var grad = new LinearGradientBrush(Color.FromArgb(70, sb.Color.R, sb.Color.G, sb.Color.B), Color.FromArgb(0, sb.Color.R, sb.Color.G, sb.Color.B), 90);
            dc.DrawGeometry(grad, null, fill);
        }
        dc.DrawGeometry(null, pen, geo);
    }
}
