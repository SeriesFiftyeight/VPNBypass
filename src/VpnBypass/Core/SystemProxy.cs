using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VpnBypass.Core;

/// <summary>
/// Points the per-user Windows proxy (WinINET: browsers, Discord, most apps) straight at our
/// engine through the official per-connection API, which updates the binary
/// DefaultConnectionSettings blob that Chrome/Electron actually read (plain registry values alone
/// are ignored by them). A fixed proxy is used because PAC scripts get cached as "failed" by
/// Chrome and silently go DIRECT. Since a fixed proxy would cut the internet if we died, the
/// settings are restored on exit, on crash, on next launch and by a watchdog process that
/// outlives a forced kill. The user's previous settings are backed up first. No admin needed.
/// </summary>
public static class SystemProxy
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const string ConnKeyPath = KeyPath + @"\Connections";
    private const string BackupKeyPath = @"Software\VPNBypass\ProxyBackup";
    private const string Bypass = "localhost;127.*;10.*;192.168.*;169.254.*;*.local;<local>";

    private const int FlagDirect = 1, FlagProxy = 2, FlagPac = 4, FlagAutoDetect = 8;

    public static string ServerFor(int port) => $"127.0.0.1:{port}";

    public static void Enable(int port)
    {
        string server = ServerFor(port);

        // Back up the user's own settings (not our own leftovers).
        if (!IsOurs())
        {
            using var backup = Registry.CurrentUser.CreateSubKey(BackupKeyPath);
            var s = ReadBlobStrings();
            backup.SetValue("Flags", ReadFlags(), RegistryValueKind.DWord);
            Save(backup, "ProxyServer", s[0]);
            Save(backup, "ProxyOverride", s[1]);
            Save(backup, "AutoConfigURL", s[2]);
        }
        using (var backup = Registry.CurrentUser.CreateSubKey(BackupKeyPath))
            backup.SetValue("Ours", server, RegistryValueKind.String);

        Apply(FlagDirect | FlagProxy, server, Bypass, null);
    }

    /// <summary>Restores the backed-up settings if they still point at us. Safe to call any time.</summary>
    public static void Restore()
    {
        try
        {
            if (!IsOurs()) return;
            using var backup = Registry.CurrentUser.OpenSubKey(BackupKeyPath);
            int flags = backup?.GetValue("Flags") is int f ? f : FlagDirect | FlagAutoDetect;
            string? server = backup?.GetValue("ProxyServer") as string;
            string? bypass = backup?.GetValue("ProxyOverride") as string;
            string? pac = backup?.GetValue("AutoConfigURL") as string;

            // Never hand back anything that points at us (e.g. a backup taken from a stale run).
            if (server?.StartsWith("127.0.0.1:", StringComparison.Ordinal) == true) { server = null; flags &= ~FlagProxy; }
            if (pac?.StartsWith("http://127.0.0.1:", StringComparison.Ordinal) == true) { pac = null; flags &= ~FlagPac; }

            Apply(flags | FlagDirect, server, bypass, pac);
        }
        catch
        {
            // Registry/WinINET unavailable; nothing else we can do.
        }
    }

    /// <summary>Kept for apps that still fetch http://127.0.0.1:PORT/proxy.pac.</summary>
    public static string BuildPac(int port) => $$"""
        function FindProxyForURL(url, host) {
          if (isPlainHostName(host) || host == "localhost" ||
              shExpMatch(host, "127.*") || shExpMatch(host, "10.*") ||
              shExpMatch(host, "192.168.*") || shExpMatch(host, "*.local"))
            return "DIRECT";
          return "PROXY 127.0.0.1:{{port}}; DIRECT";
        }
        """;

    private static bool IsOurs()
    {
        using var backup = Registry.CurrentUser.OpenSubKey(BackupKeyPath);
        if (backup?.GetValue("Ours") is not string ours) return false;
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        // Current marker is "127.0.0.1:PORT" in the connection blob; older builds used a PAC URL
        // or the plain registry values.
        string? server = ReadBlobServer();
        return (ReadFlags() & FlagProxy) != 0 && (server == ours || server?.StartsWith("127.0.0.1:", StringComparison.Ordinal) == true)
            || key?.GetValue("AutoConfigURL") as string == ours
            || key?.GetValue("ProxyEnable") is 1 && key.GetValue("ProxyServer") as string == ours;
    }

    private static byte[]? ReadBlob()
    {
        using var conn = Registry.CurrentUser.OpenSubKey(ConnKeyPath);
        return conn?.GetValue("DefaultConnectionSettings") as byte[];
    }

    /// <summary>Flags byte of the LAN connection blob (what WinINET/Chrome really use).</summary>
    private static int ReadFlags() => ReadBlob() is { Length: > 8 } b ? b[8] : FlagDirect;

    private static string? ReadBlobServer() => ReadBlobStrings()[0];

    /// <summary>[server, bypass, pac] from the blob: from offset 12, three (DWORD length, ASCII) pairs.</summary>
    private static string?[] ReadBlobStrings()
    {
        var result = new string?[3];
        if (ReadBlob() is not { } b) return result;
        int o = 12;
        for (int i = 0; i < 3 && o + 4 <= b.Length; i++)
        {
            int len = BitConverter.ToInt32(b, o);
            o += 4;
            if (len < 0 || o + len > b.Length) break;
            if (len > 0) result[i] = System.Text.Encoding.ASCII.GetString(b, o, len);
            o += len;
        }
        return result;
    }

    private static void Save(RegistryKey to, string name, string? value)
    {
        if (value != null) to.SetValue(name, value, RegistryValueKind.String);
        else to.DeleteValue(name, throwOnMissingValue: false);
    }

    // ── WinINET per-connection options ──

    private const int INTERNET_OPTION_REFRESH = 37;
    private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
    private const int INTERNET_OPTION_PER_CONNECTION_OPTION = 75;
    private const int INTERNET_PER_CONN_FLAGS = 1, INTERNET_PER_CONN_PROXY_SERVER = 2,
                      INTERNET_PER_CONN_PROXY_BYPASS = 3, INTERNET_PER_CONN_AUTOCONFIG_URL = 4;

    private static void Apply(int flags, string? server, string? bypass, string? pac)
    {
        // INTERNET_PER_CONN_OPTION = { DWORD dwOption; union { DWORD; LPWSTR; FILETIME } Value; }
        int optSize = IntPtr.Size == 8 ? 16 : 12;
        int valOffset = IntPtr.Size == 8 ? 8 : 4;
        var strings = new List<IntPtr>();
        IntPtr options = Marshal.AllocHGlobal(optSize * 4);
        IntPtr list = IntPtr.Zero;
        try
        {
            void SetOpt(int i, int option, IntPtr value)
            {
                IntPtr p = options + i * optSize;
                for (int z = 0; z < optSize; z++) Marshal.WriteByte(p, z, 0);
                Marshal.WriteInt32(p, option);
                Marshal.WriteIntPtr(p, valOffset, value);
            }
            IntPtr Str(string? s)
            {
                IntPtr ptr = Marshal.StringToHGlobalUni(s ?? "");
                strings.Add(ptr);
                return ptr;
            }

            SetOpt(0, INTERNET_PER_CONN_FLAGS, flags);
            SetOpt(1, INTERNET_PER_CONN_PROXY_SERVER, Str(server));
            SetOpt(2, INTERNET_PER_CONN_PROXY_BYPASS, Str(bypass));
            SetOpt(3, INTERNET_PER_CONN_AUTOCONFIG_URL, Str(pac));

            // INTERNET_PER_CONN_OPTION_LIST = { DWORD dwSize; LPWSTR pszConnection; DWORD dwOptionCount; DWORD dwOptionError; LPINTERNET_PER_CONN_OPTION pOptions; }
            int listSize = IntPtr.Size == 8 ? 32 : 20;
            list = Marshal.AllocHGlobal(listSize);
            int o = 0;
            Marshal.WriteInt32(list, o, listSize); o += IntPtr.Size == 8 ? 8 : 4;
            Marshal.WriteIntPtr(list, o, IntPtr.Zero); o += IntPtr.Size;           // null = LAN
            Marshal.WriteInt32(list, o, 4); o += 4;
            Marshal.WriteInt32(list, o, 0); o += 4;
            Marshal.WriteIntPtr(list, o, options);

            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_PER_CONNECTION_OPTION, list, listSize);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }
        finally
        {
            foreach (var s in strings) Marshal.FreeHGlobal(s);
            Marshal.FreeHGlobal(options);
            if (list != IntPtr.Zero) Marshal.FreeHGlobal(list);
        }
    }

    [DllImport("wininet.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "InternetSetOptionW")]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);
}
