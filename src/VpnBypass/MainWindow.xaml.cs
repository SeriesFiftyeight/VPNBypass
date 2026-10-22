using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using VpnBypass.Core;

namespace VpnBypass;

public partial class MainWindow : Window
{
    public static readonly DependencyProperty GoodLimitProperty =
        DependencyProperty.Register(nameof(GoodLimit), typeof(double), typeof(MainWindow), new PropertyMetadata(60.0));
    public static readonly DependencyProperty CriticalLimitProperty =
        DependencyProperty.Register(nameof(CriticalLimit), typeof(double), typeof(MainWindow), new PropertyMetadata(150.0));

    public double GoodLimit { get => (double)GetValue(GoodLimitProperty); set => SetValue(GoodLimitProperty, value); }
    public double CriticalLimit { get => (double)GetValue(CriticalLimitProperty); set => SetValue(CriticalLimitProperty, value); }

    private const int MaxLogLines = 400;
    private const int LossWindow = 20;

    private readonly AppSettings _cfg = AppSettings.Current;
    private readonly BypassEngine _engine = BypassEngine.Instance;
    private readonly PingMonitor _ping = new();
    private readonly ObservableCollection<PingRow> _rows = [];
    private readonly ObservableCollection<LogLine> _log = [];
    private readonly Queue<double?> _avgHistory = new();
    private readonly Queue<(int Lost, int Total)> _lossHistory = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _glitch = new();
    private readonly Random _rng = new();

    private bool _busy;
    private int _highPingStreak;
    private DateTime _lastAlert = DateTime.MinValue;
    private bool _trayHintShown;

    public MainWindow()
    {
        InitializeComponent();
        PingList.ItemsSource = _rows;
        LogList.ItemsSource = _log;

        _engine.Log += (lvl, msg) => Dispatcher.BeginInvoke(() => LogLine(lvl, msg));
        _engine.StatusChanged += _ => Dispatcher.BeginInvoke(UpdateState);
        _ping.Round += samples => Dispatcher.BeginInvoke(() => OnPingRound(samples));
        Loc.Changed += OnLanguageChanged;

        _tick.Tick += (_, _) => OnTick();
        _tick.Start();
        _glitch.Tick += (_, _) => Glitch();
        _glitch.Interval = TimeSpan.FromSeconds(3);
        _glitch.Start();

        Caret.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
        {
            RepeatBehavior = RepeatBehavior.Forever,
            KeyFrames = { new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(0)), new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(0.5)) },
            Duration = TimeSpan.FromSeconds(1)
        });

        bool admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        SysInfo.Text = $"ENGINE  goodbyedpi 0.2.3\nDRIVER  windivert {(Environment.Is64BitOperatingSystem ? "x64" : "x86")}\nPRIV    {(admin ? "ADMIN" : "USER (!)")}\nNODE    {Environment.MachineName}";

        StateChanged += (_, _) =>
        {
            // WindowChrome lets a maximized window overhang the screen by the resize border.
            Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
        };

        LoadSettingsUi();
        BuildProfileCards();
        BuildLegend();
        ApplyLimits();
        UpdateState();
        OnTick();
    }

    // ───────────────────────── lifecycle ─────────────────────────

    public async Task BootAsync(bool autoConnect)
    {
        string[] boot =
        [
            "initializing VPN BYPASS kernel v2.0",
            $"engine path :: {BypassEngine.EnginePath}",
            "windivert packet filter :: ready",
            $"latency matrix :: {_cfg.PingTargets.Count} nodes @ {_cfg.PingIntervalSec}s"
        ];
        foreach (var line in boot)
        {
            LogLine(LogLevel.Info, line);
            await Task.Delay(70);
        }
        _ping.Start();
        if (autoConnect) await ConnectAsync();
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_cfg.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            if (!_trayHintShown)
            {
                TrayIcon.Instance?.Notify(Loc.T("TrayHidden"));
                _trayHintShown = true;
            }
            return;
        }
        e.Cancel = true;
        ((App)Application.Current).ExitApp();
    }

    // ───────────────────────── engine ─────────────────────────

    public Task ToggleAsync() => _engine.IsRunning ? DisconnectAsync() : ConnectAsync();

    private async Task ConnectAsync()
    {
        if (_busy) return;
        _busy = true;
        UpdateState();
        bool ok = await _engine.StartAsync(Presets.Get(_cfg.PresetId));
        _busy = false;
        UpdateState();
        if (!ok) TrayIcon.Instance?.Notify(Loc.T("EngineFail"), warning: true);
    }

    private async Task DisconnectAsync()
    {
        if (_busy) return;
        _busy = true;
        UpdateState();
        await _engine.StopAsync();
        _busy = false;
        UpdateState();
    }

    private void UpdateState()
    {
        bool on = _engine.IsRunning;
        var neon = (Brush)FindResource("Neon");
        var red = (Brush)FindResource("Red");
        var cyan = (Brush)FindResource("Cyan");

        Eye.SetActive(on);

        string stateKey = _busy ? "StateBusy" : on ? "StateActive" : "StateIdle";
        StateText.SetResourceReference(TextBlock.TextProperty, stateKey);
        StateText.Foreground = _busy ? cyan : on ? neon : red;
        StateGlow.Color = ((SolidColorBrush)StateText.Foreground).Color;
        StateSub.SetResourceReference(TextBlock.TextProperty, on ? "StateActiveSub" : "StateIdleSub");

        ConnectButton.SetResourceReference(ContentProperty, on ? "BtnDisconnect" : "BtnConnect");
        ConnectButton.Background = on ? red : neon;
        ConnectButton.IsEnabled = !_busy;

        StatusDot.Fill = on ? neon : red;
        StatusDot.Effect = (Effect)FindResource(on ? "NeonGlow" : "RedGlow");
        StatusText.Foreground = on ? neon : red;
        StatusText.SetResourceReference(TextBlock.TextProperty, on ? "StatusOnline" : "StatusOffline");

        StatProfile.Text = (on ? _engine.ActivePreset : Presets.Get(_cfg.PresetId)).Code;
        Title = on ? "VPN Bypass · ACTIVE" : "VPN Bypass";
    }

    private void OnTick()
    {
        Clock.Text = DateTime.Now.ToString("HH:mm:ss");
        StatUptime.Text = _engine.StartedAt is DateTime t ? (DateTime.Now - t).ToString(@"hh\:mm\:ss") : "--:--:--";
    }

    // ───────────────────────── ping ─────────────────────────

    private void OnPingRound(IReadOnlyList<PingSample> samples)
    {
        if (_rows.Count != samples.Count || _rows.Where((r, i) => r.Host != samples[i].Target.Host).Any())
        {
            _rows.Clear();
            foreach (var s in samples) _rows.Add(new PingRow(s.Target));
        }
        for (int i = 0; i < samples.Count; i++) _rows[i].Push(samples[i], _cfg);

        var ok = samples.Where(s => s.Ms.HasValue).Select(s => (double)s.Ms!.Value).ToList();
        double? avg = ok.Count > 0 ? Math.Round(ok.Average()) : null;

        _avgHistory.Enqueue(avg);
        while (_avgHistory.Count > 60) _avgHistory.Dequeue();
        AvgSpark.Values = _avgHistory.ToArray();

        var quality = PingMonitor.Classify((long?)avg, _cfg);
        var brush = QualityBrushes.For(quality);
        AvgValue.Text = avg?.ToString("0") ?? "--";
        AvgValue.Foreground = brush;
        AvgGlow.Color = ((SolidColorBrush)brush).Color;
        AvgSpark.Stroke = brush;
        AvgLabel.Text = "· " + Loc.T(QualityBrushes.LabelKey(quality));
        AvgLabel.Foreground = brush;
        StatPing.Text = avg is double a ? $"{a:0} ms" : "--";
        StatPing.Foreground = brush;

        _lossHistory.Enqueue((samples.Count - ok.Count, samples.Count));
        while (_lossHistory.Count > LossWindow) _lossHistory.Dequeue();
        int total = _lossHistory.Sum(x => x.Total);
        double loss = total == 0 ? 0 : 100.0 * _lossHistory.Sum(x => x.Lost) / total;
        StatLoss.Text = $"{loss:0.#}%";
        StatLoss.Foreground = (Brush)FindResource(loss < 1 ? "Neon" : loss < 10 ? "Amber" : "Red");

        CheckHighPing(avg);
    }

    private void CheckHighPing(double? avg)
    {
        _highPingStreak = avg > _cfg.PingCriticalMs ? _highPingStreak + 1 : 0;
        if (_highPingStreak < 3 || DateTime.Now - _lastAlert < TimeSpan.FromMinutes(2)) return;

        _lastAlert = DateTime.Now;
        string msg = string.Format(Loc.T("HighPingAlert"), avg, _cfg.PingCriticalMs);
        LogLine(LogLevel.Warn, msg);
        if (_cfg.AlertOnHighPing) TrayIcon.Instance?.Notify(msg, warning: true);
    }

    private void ApplyLimits()
    {
        GoodLimit = _cfg.PingGoodMs;
        CriticalLimit = _cfg.PingCriticalMs;
        BuildLegend();
    }

    private void BuildLegend()
    {
        Legend.Children.Clear();
        (string Brush, string Text)[] items =
        [
            ("Neon", $"≤{_cfg.PingGoodMs} {Loc.T("PingGood")}"),
            ("Amber", $"≤{_cfg.PingCriticalMs} {Loc.T("PingWarn")}"),
            ("Red", $">{_cfg.PingCriticalMs} {Loc.T("PingBad")}")
        ];
        foreach (var (b, text) in items)
        {
            Legend.Children.Add(new LegendDot(b));
            Legend.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("Label"), Margin = new Thickness(6, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center });
        }
    }

    private sealed class LegendDot : System.Windows.Shapes.Shape
    {
        private static readonly Geometry Dot = new EllipseGeometry(new Point(3.5, 3.5), 3.5, 3.5);
        public LegendDot(string brushKey)
        {
            SetResourceReference(FillProperty, brushKey);
            Width = 7; Height = 7;
            VerticalAlignment = VerticalAlignment.Center;
        }
        protected override Geometry DefiningGeometry => Dot;
    }

    // ───────────────────────── log ─────────────────────────

    public void LogLine(LogLevel level, string text)
    {
        var (tag, key) = level switch
        {
            LogLevel.Ok => ("[ + ] ", "Neon"),
            LogLevel.Warn => ("[ ! ] ", "Amber"),
            LogLevel.Error => ("[ x ] ", "Red"),
            LogLevel.Engine => ("[eng] ", "TextDim"),
            _ => ("[ * ] ", "Cyan")
        };
        _log.Add(new LogLine { Time = DateTime.Now.ToString("HH:mm:ss "), Tag = tag, Text = text, Brush = (Brush)FindResource(key) });
        while (_log.Count > MaxLogLines) _log.RemoveAt(0);
        LogList.ScrollIntoView(_log[^1]);
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => _log.Clear();

    // ───────────────────────── profiles ─────────────────────────

    private void BuildProfileCards()
    {
        ProfileGrid.Children.Clear();
        foreach (var p in Presets.All)
        {
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var code = new Border
            {
                BorderBrush = (Brush)FindResource("Cyan"), BorderThickness = new Thickness(1), Padding = new Thickness(6, 1, 6, 1), CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock { Text = p.Code, Foreground = (Brush)FindResource("Cyan"), FontSize = 10.5, FontWeight = FontWeights.Bold }
            };
            header.Children.Add(code);
            if (p.Recommended)
            {
                var rec = new TextBlock { Text = "★ " + Loc.T("Recommended"), Foreground = (Brush)FindResource("Neon"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
                DockPanel.SetDock(rec, Dock.Right);
                header.Children.Insert(0, rec);
            }

            var body = new StackPanel();
            body.Children.Add(header);
            body.Children.Add(new TextBlock { Text = p.Name, FontFamily = (FontFamily)FindResource("Display"), FontWeight = FontWeights.Bold, FontSize = 17, Foreground = (Brush)FindResource("Text") });
            body.Children.Add(new TextBlock { Text = p.Description, Style = (Style)FindResource("Label"), FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10) });
            body.Children.Add(new TextBlock { Text = "$ goodbyedpi " + p.Arguments, Foreground = (Brush)FindResource("TextFaint"), FontSize = 10, TextWrapping = TextWrapping.Wrap });

            var card = new RadioButton
            {
                Style = (Style)FindResource("ProfileCard"),
                GroupName = "profiles",
                Content = body,
                IsChecked = p.Id == _cfg.PresetId,
                Tag = p.Id
            };
            card.Checked += Profile_Checked;
            ProfileGrid.Children.Add(card);
        }
    }

    private async void Profile_Checked(object sender, RoutedEventArgs e)
    {
        int id = (int)((RadioButton)sender).Tag;
        if (id == _cfg.PresetId) return;
        _cfg.PresetId = id;
        _cfg.Save();
        var preset = Presets.Get(id);
        LogLine(LogLevel.Info, $"profile switched -> [{preset.Code}] {preset.NameEn}");
        UpdateState();
        if (_engine.IsRunning && !_busy)
        {
            _busy = true;
            UpdateState();
            await _engine.StartAsync(preset);
            _busy = false;
            UpdateState();
        }
    }

    // ───────────────────────── settings ─────────────────────────

    private void LoadSettingsUi()
    {
        OptAutoConnect.IsChecked = _cfg.AutoConnect;
        OptStartTray.IsChecked = _cfg.StartInTray;
        OptCloseTray.IsChecked = _cfg.CloseToTray;
        OptAlert.IsChecked = _cfg.AlertOnHighPing;
        InGood.Text = _cfg.PingGoodMs.ToString();
        InCrit.Text = _cfg.PingCriticalMs.ToString();
        InInterval.Text = _cfg.PingIntervalSec.ToString();
        InTargets.Text = string.Join(Environment.NewLine, _cfg.PingTargets.Select(t => $"{t.Name} = {t.Host}"));
        HighlightLanguage();
        _ = RefreshAutoStartAsync();
    }

    private async Task RefreshAutoStartAsync() => OptAutoStart.IsChecked = await SystemTools.IsAutoStartEnabledAsync();

    private void Option_Click(object sender, RoutedEventArgs e)
    {
        _cfg.AutoConnect = OptAutoConnect.IsChecked == true;
        _cfg.StartInTray = OptStartTray.IsChecked == true;
        _cfg.CloseToTray = OptCloseTray.IsChecked == true;
        _cfg.Save();
    }

    private async void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        bool want = OptAutoStart.IsChecked == true;
        bool ok = await SystemTools.SetAutoStartAsync(want);
        LogLine(ok ? LogLevel.Ok : LogLevel.Error, $"autostart task {(want ? "registered" : "removed")}{(ok ? "" : " :: FAILED")}");
        await RefreshAutoStartAsync();
    }

    private void SavePing_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(InGood.Text, out int good)) _cfg.PingGoodMs = good;
        if (int.TryParse(InCrit.Text, out int crit)) _cfg.PingCriticalMs = crit;
        if (int.TryParse(InInterval.Text, out int iv)) _cfg.PingIntervalSec = iv;
        _cfg.AlertOnHighPing = OptAlert.IsChecked == true;

        var targets = new List<PingTarget>();
        foreach (var raw in InTargets.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = raw.Split('=', 2, StringSplitOptions.TrimEntries);
            var host = parts.Length == 2 ? parts[1] : parts[0];
            var name = parts[0];
            if (host.Length > 0) targets.Add(new PingTarget { Name = name, Host = host });
        }
        if (targets.Count > 0) _cfg.PingTargets = targets;

        _cfg.Save();
        LoadSettingsUi();
        ApplyLimits();
        _avgHistory.Clear();
        _lossHistory.Clear();
        _ping.Start();
        LogLine(LogLevel.Ok, $"{Loc.T("Saved")} :: good≤{_cfg.PingGoodMs}ms crit>{_cfg.PingCriticalMs}ms every {_cfg.PingIntervalSec}s");
    }

    private void Lang_Click(object sender, RoutedEventArgs e)
    {
        _cfg.Language = (string)((Button)sender).Tag;
        _cfg.Save();
        Loc.Apply(_cfg.Language);
    }

    private void HighlightLanguage()
    {
        var neon = (Brush)FindResource("Neon");
        var line = (Brush)FindResource("LineBright");
        LangTr.BorderBrush = Loc.IsTurkish ? neon : line;
        LangEn.BorderBrush = Loc.IsTurkish ? line : neon;
        LangTr.Background = Loc.IsTurkish ? (Brush)FindResource("NeonSoft") : Brushes.Transparent;
        LangEn.Background = Loc.IsTurkish ? Brushes.Transparent : (Brush)FindResource("NeonSoft");
    }

    private void OnLanguageChanged()
    {
        HighlightLanguage();
        BuildProfileCards();
        BuildLegend();
        foreach (var r in _rows) r.RefreshLabel();
    }

    private async void FlushDns_Click(object sender, RoutedEventArgs e)
    {
        bool ok = await SystemTools.FlushDnsAsync();
        LogLine(ok ? LogLevel.Ok : LogLevel.Error, ok ? "dns resolver cache flushed" : "dns flush failed");
    }

    private async void KillEngine_Click(object sender, RoutedEventArgs e)
    {
        await _engine.StopAsync();
        BypassEngine.KillStrays();
        LogLine(LogLevel.Warn, "engine force-killed, all goodbyedpi processes terminated");
        UpdateState();
    }

    // ───────────────────────── chrome & nav ─────────────────────────

    private async void Connect_Click(object sender, RoutedEventArgs e) => await ToggleAsync();

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageControl == null) return;
        PageControl.Visibility = sender == NavControl ? Visibility.Visible : Visibility.Collapsed;
        PageModes.Visibility = sender == NavModes ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = sender == NavSettings ? Visibility.Visible : Visibility.Collapsed;
        Breadcrumb.Text = sender == NavModes ? "~/profiles" : sender == NavSettings ? "~/settings" : "~/control";

        var page = sender == NavModes ? (FrameworkElement)PageModes : sender == NavSettings ? PageSettings : PageControl;
        page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
    }

    private void Glitch()
    {
        _glitch.Interval = TimeSpan.FromSeconds(2.5 + _rng.NextDouble() * 4);
        var dur = new Duration(TimeSpan.FromMilliseconds(180));
        foreach (var (layer, shift) in new[] { (GlitchA, GlitchAX), (GlitchB, GlitchBX) })
        {
            layer.BeginAnimation(OpacityProperty, new DoubleAnimationUsingKeyFrames
            {
                Duration = dur,
                KeyFrames = { new DiscreteDoubleKeyFrame(0.85, KeyTime.FromPercent(0)), new DiscreteDoubleKeyFrame(0.4, KeyTime.FromPercent(0.5)), new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(1)) }
            });
            double d = (_rng.NextDouble() * 3 + 1.5) * (layer == GlitchA ? -1 : 1);
            shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimationUsingKeyFrames
            {
                Duration = dur,
                KeyFrames = { new DiscreteDoubleKeyFrame(d, KeyTime.FromPercent(0)), new DiscreteDoubleKeyFrame(-d / 2, KeyTime.FromPercent(0.5)), new DiscreteDoubleKeyFrame(0, KeyTime.FromPercent(1)) }
            });
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
