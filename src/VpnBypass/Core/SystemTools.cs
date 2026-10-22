using System.Diagnostics;

namespace VpnBypass.Core;

public static class SystemTools
{
    private const string TaskName = "VPNBypass";

    public static async Task<bool> FlushDnsAsync() => (await RunAsync("ipconfig", "/flushdns")).ExitCode == 0;

    // A requireAdministrator exe is silently skipped by the Registry Run key,
    // so autostart uses a logon task with highest privileges instead.
    public static async Task<bool> IsAutoStartEnabledAsync() =>
        (await RunAsync("schtasks", $"/Query /TN \"{TaskName}\"")).ExitCode == 0;

    public static async Task<bool> SetAutoStartAsync(bool enable)
    {
        if (!enable)
            return (await RunAsync("schtasks", $"/Delete /TN \"{TaskName}\" /F")).ExitCode == 0;

        string exe = Environment.ProcessPath ?? "";
        string args = $"/Create /TN \"{TaskName}\" /TR \"\\\"{exe}\\\" --tray\" /SC ONLOGON /RL HIGHEST /F";
        return (await RunAsync("schtasks", args)).ExitCode == 0;
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string file, string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (p == null) return (-1, "");
            string output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return (p.ExitCode, output);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
