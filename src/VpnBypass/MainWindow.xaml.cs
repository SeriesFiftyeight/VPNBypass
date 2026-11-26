using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

    private bool _busy;
    private bool _tuning;
    private bool _ipv4 = true;
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
        _ping.Round += (samples, ipv4) => Dispatcher.BeginInvoke(() => OnPingRound(samples, ipv4));
        Loc.Changed += OnLanguageChanged;

        _tick.Tick += (_, _) => OnTick();
        _tick.Start();

        LoadSettingsUi();
        BuildProfileList();
        BuildDnsList();
        ApplyLimits();
        UpdateState();
    }

    // ───────────────────────── lifecycle ─────────────────────────

    public async Task BootAsync(bool autoConnect)
    {
        LogLine(LogLevel.Info, "VPN Bypass engine v2.1 :: user-mode desync proxy + DoH");
        LogLine(LogLevel.Info, $"network :: IPv4 {(NetworkState.HasIpv4 ? "up" : "DOWN")} · IPv6 {(NetworkState.HasIpv6 ? "up" : "down")}");
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
        e.Cancel = true;
        if (_cfg.CloseToTray)
        {
            Hide();
            if (!_trayHintShown)
            {
                TrayIcon.Instance?.Notify(Loc.T("TrayHidden"));
                _trayHintShown = true;
            }
            return;
        }
        ((App)Application.Current).ExitApp();
    }

    // ───────────────────────── engine ─────────────────────────

    public Task ToggleAsync() => _engine.IsRunning ? DisconnectAsync() : ConnectAsync();

    private async Task ConnectAsync()
    {
        if (_busy) return;
        _busy = true;
        UpdateState();
        bool ok = await _engine.StartAsync(Profiles.Get(_cfg.ProfileId));
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
        var accent = _busy ? (Brush)FindResource("Cyan") : on ? neon : red;

        StatusPill.BorderBrush = accent;
        StatusPill.Background = (Brush)FindResource(on ? "NeonSoft" : "RedSoft");
        StatusDot.Fill = accent;
        StatusText.Foreground = accent;
        StatusText.SetResourceReference(TextBlock.TextProperty, on ? "StatusOnline" : "StatusOffline");

        ConnectButton.Foreground = on ? red : neon;
        ConnectButton.IsEnabled = !_busy;
        ConnectLabel.SetResourceReference(TextBlock.TextProperty, on ? "BtnDisconnect" : "BtnConnect");
        StateText.SetResourceReference(TextBlock.TextProperty, _busy ? "StateBusy" : on ? "StateActive" : "StateIdle");
        StateText.Foreground = on ? neon : (Brush)FindResource("Text");

        // Calm when idle, a slow orbit + glow when protected.
        Orbit.BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 0.6 : 0, TimeSpan.FromMilliseconds(400)));
        Halo.BeginAnimation(OpacityProperty, on
            ? new DoubleAnimation(0.10, 0.22, TimeSpan.FromSeconds(2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }
            : new DoubleAnimation(0, TimeSpan.FromMilliseconds(400)));
        if (on) OrbitSpin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(14)) { RepeatBehavior = RepeatBehavior.Forever });
        else OrbitSpin.BeginAnimation(RotateTransform.AngleProperty, null);

        Title = on ? "VPN Bypass · ON" : "VPN Bypass";
        OnTick();
    }

    private void OnTick()
    {
        var p = _engine.IsRunning ? _engine.ActiveProfile : Profiles.Get(_cfg.ProfileId);
        string uptime = _engine.StartedAt is DateTime t ? (DateTime.Now - t).ToString(@"hh\:mm\:ss") : "--:--:--";
        StateSub.Text = $"{p.Code} · {p.Name} · {uptime}";
        StatTraffic.Text = FormatBytes(_engine.BytesTotal);
    }

    private static string FormatBytes(long b) => b switch
    {
        < 1024 * 1024 => $"{b / 1024.0:0} KB",
        < 1024L * 1024 * 1024 => $"{b / 1024.0 / 1024:0.0} MB",
        _ => $"{b / 1024.0 / 1024 / 1024:0.00} GB"
    };

    // ───────────────────────── ping ─────────────────────────

    private void OnPingRound(IReadOnlyList<PingSample> samples, bool ipv4)
    {
        if (ipv4 != _ipv4)
        {
            _ipv4 = ipv4;
            LogLine(ipv4 ? LogLevel.Ok : LogLevel.Warn, Loc.T(ipv4 ? "Ipv4Back" : "Ipv4Lost"));
            if (!ipv4) TrayIcon.Instance?.Notify(Loc.T("Ipv4Lost"), warning: true);
        }

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
        AvgSpark.Stroke = brush;
        AvgLabel.Text = $"{Loc.T("PingAverage")} {(avg is double v ? $"{v:0} ms" : "--")} · {Loc.T(QualityBrushes.LabelKey(quality))}";
        AvgLabel.Foreground = brush;
        StatPing.Text = avg is double a ? $"{a:0} ms" : "--";
        StatPing.Foreground = brush;

        // Targets skipped for lack of IPv4 were never sent, so they don't count as packet loss.
        int measured = samples.Count(s => s.Quality != PingQuality.NoIpv4);
        _lossHistory.Enqueue((measured - ok.Count, measured));
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
            ("Neon", $"≤{_cfg.PingGoodMs}"),
            ("Amber", $"≤{_cfg.PingCriticalMs}"),
            ("Red", $">{_cfg.PingCriticalMs}")
        ];
        foreach (var (b, text) in items)
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, b);
            Legend.Children.Add(dot);
            Legend.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("Label"), FontSize = 9.5, Margin = new Thickness(4, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        }
    }

    // ───────────────────────── log ─────────────────────────

    public void LogLine(LogLevel level, string text)
    {
        var (tag, key) = level switch
        {
            LogLevel.Ok => ("[+] ", "Neon"),
            LogLevel.Warn => ("[!] ", "Amber"),
            LogLevel.Error => ("[x] ", "Red"),
            LogLevel.Engine => ("[e] ", "TextDim"),
            _ => ("[*] ", "Cyan")
        };
        _log.Add(new LogLine { Time = DateTime.Now.ToString("HH:mm:ss "), Tag = tag, Text = text, Brush = (Brush)FindResource(key) });
        while (_log.Count > MaxLogLines) _log.RemoveAt(0);
        LogList.ScrollIntoView(_log[^1]);
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e) => _log.Clear();

    // ───────────────────────── profiles & dns ─────────────────────────

    private void BuildProfileList()
    {
        ProfileList.Children.Clear();
        foreach (var p in Profiles.All)
        {
            var header = new DockPanel();
            header.Children.Add(new Border
            {
                BorderBrush = (Brush)FindResource("Cyan"), BorderThickness = new Thickness(1), Padding = new Thickness(5, 0, 5, 0), CornerRadius = new CornerRadius(2),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
                Child = new TextBlock { Text = p.Code, Foreground = (Brush)FindResource("Cyan"), FontSize = 10, FontWeight = FontWeights.Bold }
            });
            header.Children.Add(new TextBlock { Text = p.Name, FontFamily = (FontFamily)FindResource("Display"), FontWeight = FontWeights.Bold, FontSize = 14, Foreground = (Brush)FindResource("Text"), VerticalAlignment = VerticalAlignment.Center });

            var body = new StackPanel();
            body.Children.Add(header);
            body.Children.Add(new TextBlock { Text = p.Description, Style = (Style)FindResource("Label"), FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 4) });
            body.Children.Add(new TextBlock { Text = p.Recipe, Foreground = (Brush)FindResource("TextFaint"), FontSize = 9.5 });

            var row = new RadioButton { Style = (Style)FindResource("ChoiceRow"), GroupName = "profiles", Content = body, IsChecked = p.Id == _cfg.ProfileId, Tag = p.Id };
            row.Checked += Profile_Checked;
            ProfileList.Children.Add(row);
        }
    }

    private void Profile_Checked(object sender, RoutedEventArgs e) => SelectProfile((int)((RadioButton)sender).Tag);

    private void SelectProfile(int id)
    {
        if (id == _cfg.ProfileId && _engine.ActiveProfile.Id == id) return;
        _cfg.ProfileId = id;
        _cfg.Save();
        _engine.SetProfile(Profiles.Get(id));
        foreach (RadioButton r in ProfileList.Children) r.IsChecked = (int)r.Tag == id;
        OnTick();
    }

    private void BuildDnsList()
    {
        DnsList.Children.Clear();
        foreach (var d in DohResolver.Providers)
        {
            var row = new RadioButton
            {
                Style = (Style)FindResource("ChoiceRow"), GroupName = "dns", Tag = d.Id, IsChecked = d.Id == _cfg.DnsProvider,
                Margin = new Thickness(0, 0, 8, 8),
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = d.Name, FontWeight = FontWeights.Bold, FontSize = 12, Foreground = (Brush)FindResource("Text") },
                        new TextBlock { Text = d.Host, Style = (Style)FindResource("Label"), FontSize = 9.5, Margin = new Thickness(0, 2, 0, 0) }
                    }
                }
            };
            row.Checked += (s, _) =>
            {
                _cfg.DnsProvider = (string)((RadioButton)s).Tag;
                _cfg.Save();
                DohResolver.Instance.ClearCache();
                LogLine(LogLevel.Info, $"dns :: {DohResolver.Get(_cfg.DnsProvider).Name} DoH");
            };
            DnsList.Children.Add(row);
        }
    }

    private async void AutoFind_Click(object sender, RoutedEventArgs e)
    {
        if (_tuning) return;
        _tuning = true;
        AutoFindButton.IsEnabled = false;
        AutoFindText.Visibility = Visibility.Visible;
        AutoFindText.Text = Loc.T("AutoFindRunning");
        LogLine(LogLevel.Info, $"auto-tune :: testing {Profiles.All.Count} profiles against {string.Join(", ", AutoTuner.TestHosts)}");
        try
        {
            var results = await AutoTuner.RunAsync(
                r => Dispatcher.BeginInvoke(() => LogLine(r.Passed > 0 ? LogLevel.Ok : LogLevel.Warn,
                    $"auto-tune :: {r.Profile?.Code ?? "DIRECT"} {r.Passed}/{r.Total}{(r.AvgMs >= 0 ? $" · {r.AvgMs} ms" : "")}")),
                CancellationToken.None);

            var best = AutoTuner.Pick(results);
            bool directOk = results.First().Passed == results.First().Total;
            if (best?.Profile is { } p)
            {
                SelectProfile(p.Id);
                AutoFindText.Text = string.Format(Loc.T(directOk ? "AutoFindNoBlock" : "AutoFindDone"), p.Code, p.Name, best.Passed, best.Total);
                AutoFindText.Foreground = (Brush)FindResource("Neon");
            }
            else
            {
                AutoFindText.Text = Loc.T(NetworkState.HasIpv4 ? "AutoFindNone" : "AutoFindNoIpv4");
                AutoFindText.Foreground = (Brush)FindResource("Red");
            }
        }
        finally
        {
            _tuning = false;
            AutoFindButton.IsEnabled = true;
        }
    }

    // ───────────────────────── settings ─────────────────────────

    private void LoadSettingsUi()
    {
        OptAutoConnect.IsChecked = _cfg.AutoConnect;
        OptAutoStart.IsChecked = SystemTools.IsAutoStartEnabled();
        OptStartTray.IsChecked = _cfg.StartInTray;
        OptCloseTray.IsChecked = _cfg.CloseToTray;
        OptAlert.IsChecked = _cfg.AlertOnHighPing;
        InGood.Text = _cfg.PingGoodMs.ToString();
        InCrit.Text = _cfg.PingCriticalMs.ToString();
        InInterval.Text = _cfg.PingIntervalSec.ToString();
        InTargets.Text = string.Join(Environment.NewLine, _cfg.PingTargets.Select(t => $"{t.Name} = {t.Host}"));
        HighlightLanguage();
    }

    private void Option_Click(object sender, RoutedEventArgs e)
    {
        _cfg.AutoConnect = OptAutoConnect.IsChecked == true;
        _cfg.StartInTray = OptStartTray.IsChecked == true;
        _cfg.CloseToTray = OptCloseTray.IsChecked == true;
        _cfg.Save();
    }

    private void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        bool want = OptAutoStart.IsChecked == true;
        bool ok = SystemTools.SetAutoStart(want);
        LogLine(ok ? LogLevel.Ok : LogLevel.Error, $"autostart {(want ? "on" : "off")}{(ok ? "" : " :: FAILED")}");
        OptAutoStart.IsChecked = SystemTools.IsAutoStartEnabled();
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
            if (host.Length > 0) targets.Add(new PingTarget { Name = parts[0], Host = host });
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
        var soft = (Brush)FindResource("NeonSoft");
        LangTr.BorderBrush = Loc.IsTurkish ? neon : line;
        LangEn.BorderBrush = Loc.IsTurkish ? line : neon;
        LangTr.Background = Loc.IsTurkish ? soft : Brushes.Transparent;
        LangEn.Background = Loc.IsTurkish ? Brushes.Transparent : soft;
    }

    private void OnLanguageChanged()
    {
        HighlightLanguage();
        BuildProfileList();
        foreach (var r in _rows) r.RefreshLabel();
        OnTick();
    }

    private async void FlushDns_Click(object sender, RoutedEventArgs e)
    {
        bool ok = await SystemTools.FlushDnsAsync();
        LogLine(ok ? LogLevel.Ok : LogLevel.Error, ok ? "dns caches flushed (windows + DoH)" : "dns flush failed");
    }

    private async void ResetProxy_Click(object sender, RoutedEventArgs e)
    {
        await _engine.StopAsync();
        SystemProxy.Restore();
        LogLine(LogLevel.Warn, "engine stopped, windows proxy settings restored");
        UpdateState();
    }

    // ───────────────────────── chrome & tabs ─────────────────────────

    private async void Connect_Click(object sender, RoutedEventArgs e) => await ToggleAsync();

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (PagePing == null) return;
        PagePing.Visibility = sender == TabPing ? Visibility.Visible : Visibility.Collapsed;
        PageProfile.Visibility = sender == TabProfile ? Visibility.Visible : Visibility.Collapsed;
        PageLog.Visibility = sender == TabLog ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = sender == TabSettings ? Visibility.Visible : Visibility.Collapsed;
        if (sender == TabLog && _log.Count > 0) LogList.ScrollIntoView(_log[^1]);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
