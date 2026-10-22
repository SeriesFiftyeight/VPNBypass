using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VpnBypass.Controls;

public partial class EyeView : UserControl
{
    private static readonly Color Neon = Color.FromRgb(0x00, 0xFF, 0x9C);
    private static readonly Color Red = Color.FromRgb(0xFF, 0x3B, 0x5C);

    private bool? _active;

    public EyeView()
    {
        InitializeComponent();
        Loaded += (_, _) => StartIdleMotion();
    }

    private void StartIdleMotion()
    {
        // Look left, hold, look right, hold, back to center. No blinking, ever.
        var spline = new KeySpline(0.6, 0, 0.2, 1);
        var lookX = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(5.6) };
        lookX.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        lookX.KeyFrames.Add(new SplineDoubleKeyFrame(-46, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.55)), spline));
        lookX.KeyFrames.Add(new SplineDoubleKeyFrame(-46, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.8))));
        lookX.KeyFrames.Add(new SplineDoubleKeyFrame(46, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.5)), spline));
        lookX.KeyFrames.Add(new SplineDoubleKeyFrame(46, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.8))));
        lookX.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.4)), spline));
        Look.BeginAnimation(TranslateTransform.XProperty, lookX);

        var lookY = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(5.6) };
        lookY.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        lookY.KeyFrames.Add(new SplineDoubleKeyFrame(-5, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.55)), spline));
        lookY.KeyFrames.Add(new SplineDoubleKeyFrame(4, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.5)), spline));
        lookY.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.4)), spline));
        Look.BeginAnimation(TranslateTransform.YProperty, lookY);

        Beam.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 160, TimeSpan.FromSeconds(2.4))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            AutoReverse = true
        });

        SetActive(_active ?? false, force: true);
    }

    public void SetActive(bool active, bool force = false)
    {
        if (_active == active && !force) return;
        _active = active;
        if (!IsLoaded) return;

        var color = active ? Neon : Red;
        var brush = (SolidColorBrush)Resources["StateBrush"];
        var fade = TimeSpan.FromMilliseconds(600);
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(color, fade));
        OutlineGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.ColorProperty, new ColorAnimation(color, fade));

        // Rings spin faster while the tunnel is up.
        Spin(Ring1, 360, active ? 22 : 60);
        Spin(Ring2, -360, active ? 12 : 40);
        Spin(IrisSpin, 360, active ? 6 : 18);
    }

    private static void Spin(RotateTransform t, double to, double seconds)
    {
        double from = t.Angle % 360;
        t.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(from, from + to, TimeSpan.FromSeconds(seconds))
        {
            RepeatBehavior = RepeatBehavior.Forever
        });
    }
}
