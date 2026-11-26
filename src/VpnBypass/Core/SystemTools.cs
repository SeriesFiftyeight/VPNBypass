using System.Diagnostics;
using Microsoft.Win32;

namespace VpnBypass.Core;

public static class SystemTools
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "VPNBypass";

    public static async Task<bool> FlushDnsAsync()
    {
        DohResolver.Instance.ClearCache();
        try
        {
            using var p = Process.Start(new ProcessStartInfo("ipconfig", "/flushdns") { UseShellExecute = false, CreateNoWindow = true });
            if (p == null) return false;
            await p.WaitForExitAsync();
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    // The engine runs without admin rights, so the plain per-user Run key works.
    public static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppName) is string v && v.Contains(Environment.ProcessPath ?? "\0", StringComparison.OrdinalIgnoreCase);
    }

    public static bool SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enable) key.SetValue(AppName, $"\"{Environment.ProcessPath}\" --tray");
            else key.DeleteValue(AppName, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
