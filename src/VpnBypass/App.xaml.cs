using System.Windows;
using VpnBypass.Core;

namespace VpnBypass;

public partial class App : Application
{
    private const string MutexName = "Global\\VPNBypass_SingleInstance";
    private const string ShowEventName = "Global\\VPNBypass_Show";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private TrayIcon? _tray;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            // Wake the running instance instead of starting a second engine.
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            Shutdown();
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) => { ex.Handled = true; _window?.LogLine(LogLevel.Error, ex.Exception.Message); };

        var cfg = AppSettings.Current;
        Loc.Apply(cfg.Language);

        _window = new MainWindow();
        _tray = new TrayIcon(_window);

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        new Thread(() =>
        {
            while (_showEvent.WaitOne())
                Dispatcher.Invoke(() => _window.ShowFromTray());
        }) { IsBackground = true }.Start();

        bool hidden = e.Args.Contains("--tray") || cfg.StartInTray;
        if (!hidden) _window.Show();

        _ = _window.BootAsync(cfg.AutoConnect);
    }

    public void ExitApp()
    {
        BypassEngine.Instance.StopNow();
        _tray?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        BypassEngine.Instance.StopNow();
        _tray?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
