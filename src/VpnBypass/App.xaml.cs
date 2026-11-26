using System.Windows;
using VpnBypass.Core;

namespace VpnBypass;

public partial class App : Application
{
    private const string MutexName = "Local\\VPNBypass_SingleInstance";
    private const string ShowEventName = "Local\\VPNBypass_Show";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private TrayIcon? _tray;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length == 2 && e.Args[0] == "--watchdog" && int.TryParse(e.Args[1], out int pid))
        {
            RunWatchdog(pid);
            return;
        }
        if (e.Args.Contains("--restore"))
        {
            // Used by STOP_AND_CLEAR.bat: hand the proxy settings back and quit.
            SystemProxy.Restore();
            Shutdown();
            return;
        }

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
        // Never leave the user's internet pointing at a dead proxy.
        AppDomain.CurrentDomain.UnhandledException += (_, _) => SystemProxy.Restore();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => SystemProxy.Restore();
        SessionEnding += (_, _) => BypassEngine.Instance.StopNow();
        SystemProxy.Restore(); // leftover from a crash or forced kill
        StartWatchdog();

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

    /// <summary>
    /// A second, windowless copy of ourselves that waits for the main process to end (even by
    /// Task Manager) and then hands the user's proxy settings back, so the internet never stays
    /// pointed at a dead engine.
    /// </summary>
    private static void StartWatchdog()
    {
        try
        {
            if (Environment.ProcessPath is not { } exe) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, $"--watchdog {Environment.ProcessId}")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch
        {
            // Exit/crash handlers and the next launch still restore the settings.
        }
    }

    private void RunWatchdog(int pid)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        new Thread(() =>
        {
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById(pid);
                p.WaitForExit();
            }
            catch
            {
                // Already gone.
            }
            SystemProxy.Restore();
            Dispatcher.Invoke(Shutdown);
        }) { IsBackground = true }.Start();
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
