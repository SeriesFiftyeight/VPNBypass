using System.Diagnostics;
using System.IO;
using System.Text;

namespace VpnBypass.Core;

public enum LogLevel { Info, Ok, Warn, Error, Engine }

/// <summary>Runs and supervises the GoodbyeDPI process. Events may fire on any thread.</summary>
public sealed class BypassEngine
{
    public static BypassEngine Instance { get; } = new();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private bool _stopping;

    public bool IsRunning => _process is { HasExited: false };
    public BypassPreset ActivePreset { get; private set; } = Presets.Get(AppSettings.Current.PresetId);
    public DateTime? StartedAt { get; private set; }

    public event Action<bool>? StatusChanged;
    public event Action<LogLevel, string>? Log;

    private BypassEngine() { }

    public static string EnginePath
    {
        get
        {
            string arch = Environment.Is64BitOperatingSystem ? "x86_64" : "x86";
            return Path.Combine(AppContext.BaseDirectory, "engine", arch, "goodbyedpi.exe");
        }
    }

    public async Task<bool> StartAsync(BypassPreset? preset = null)
    {
        await _gate.WaitAsync();
        try
        {
            await Task.Run(StopCore);
            ActivePreset = preset ?? Presets.Get(AppSettings.Current.PresetId);

            string exe = EnginePath;
            if (!File.Exists(exe))
            {
                Log?.Invoke(LogLevel.Error, $"engine binary missing: {exe}");
                return false;
            }

            Log?.Invoke(LogLevel.Info, $"loading profile [{ActivePreset.Code}] :: {ActivePreset.Arguments}");

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = ActivePreset.Arguments,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var errors = new StringBuilder();
            p.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log?.Invoke(LogLevel.Engine, e.Data.Trim()); };
            p.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                lock (errors) errors.AppendLine(e.Data.Trim());
                Log?.Invoke(LogLevel.Warn, e.Data.Trim());
            };
            p.Exited += (_, _) =>
            {
                if (_stopping || !ReferenceEquals(_process, p)) return;
                StartedAt = null;
                Log?.Invoke(LogLevel.Error, $"engine terminated unexpectedly (code {SafeExitCode(p)})");
                StatusChanged?.Invoke(false);
            };

            try
            {
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                Log?.Invoke(LogLevel.Error, $"engine launch failed: {ex.Message}");
                p.Dispose();
                return false;
            }

            _process = p;

            // GoodbyeDPI exits within a few hundred ms if the WinDivert driver can't load.
            await Task.Delay(700);
            if (p.HasExited)
            {
                _process = null;
                Log?.Invoke(LogLevel.Error, "engine failed to hook network stack (admin rights / driver blocked?)");
                return false;
            }

            StartedAt = DateTime.Now;
            Log?.Invoke(LogLevel.Ok, $"tunnel ACTIVE :: pid {p.Id} :: profile {ActivePreset.Code}");
            StatusChanged?.Invoke(true);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            bool was = IsRunning;
            await Task.Run(StopCore);
            if (was) Log?.Invoke(LogLevel.Info, "tunnel closed");
            StatusChanged?.Invoke(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Synchronous stop for app shutdown.</summary>
    public void StopNow() => StopCore();

    private void StopCore()
    {
        _stopping = true;
        try
        {
            if (_process is { HasExited: false } p)
            {
                p.Kill(true);
                p.WaitForExit(2000);
            }
        }
        catch { }
        finally
        {
            _process?.Dispose();
            _process = null;
            StartedAt = null;
            KillStrays();
            _stopping = false;
        }
    }

    public static void KillStrays()
    {
        foreach (var p in Process.GetProcessesByName("goodbyedpi"))
        {
            try { p.Kill(true); p.WaitForExit(1000); } catch { }
            finally { p.Dispose(); }
        }
    }

    private static string SafeExitCode(Process p)
    {
        try { return p.ExitCode.ToString(); } catch { return "?"; }
    }
}
