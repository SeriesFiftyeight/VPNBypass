using System.Windows;
using VpnBypass.Core;
using Forms = System.Windows.Forms;

namespace VpnBypass;

public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _show = new();
    private readonly Forms.ToolStripMenuItem _toggle = new();
    private readonly Forms.ToolStripMenuItem _exit = new();
    private readonly MainWindow _window;

    public static TrayIcon? Instance { get; private set; }

    public TrayIcon(MainWindow window)
    {
        Instance = this;
        _window = window;

        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/eye.ico")).Stream;
        var menu = new Forms.ContextMenuStrip
        {
            Renderer = new Forms.ToolStripProfessionalRenderer(new DarkColors()),
            ShowImageMargin = false,
            Font = new System.Drawing.Font("Cascadia Mono", 9.5f)
        };
        foreach (var item in new[] { _show, _toggle, _exit })
            item.ForeColor = System.Drawing.Color.FromArgb(214, 255, 238);
        menu.Items.AddRange([_show, _toggle, new Forms.ToolStripSeparator(), _exit]);

        _show.Click += (_, _) => _window.ShowFromTray();
        _toggle.Click += async (_, _) => await _window.ToggleAsync();
        _exit.Click += (_, _) => ((App)Application.Current).ExitApp();

        _icon = new Forms.NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream),
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => _window.ShowFromTray();

        Loc.Changed += Refresh;
        BypassEngine.Instance.StatusChanged += _ => Application.Current.Dispatcher.BeginInvoke(Refresh);
        Refresh();
    }

    public void Refresh()
    {
        bool on = BypassEngine.Instance.IsRunning;
        _show.Text = Loc.T("TrayShow");
        _toggle.Text = Loc.T(on ? "TrayDisconnect" : "TrayConnect");
        _exit.Text = Loc.T("TrayExit");
        string text = $"VPN Bypass · {Loc.T(on ? "StatusOnline" : "StatusOffline")}";
        _icon.Text = text.Length > 63 ? text[..63] : text;
    }

    public void Notify(string text, bool warning = false) =>
        _icon.ShowBalloonTip(3000, "VPN Bypass", text, warning ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    private sealed class DarkColors : Forms.ProfessionalColorTable
    {
        private static readonly System.Drawing.Color Bg = System.Drawing.Color.FromArgb(10, 15, 20);
        private static readonly System.Drawing.Color Hover = System.Drawing.Color.FromArgb(14, 52, 40);
        private static readonly System.Drawing.Color Line = System.Drawing.Color.FromArgb(31, 92, 71);
        public override System.Drawing.Color ToolStripDropDownBackground => Bg;
        public override System.Drawing.Color MenuBorder => Line;
        public override System.Drawing.Color MenuItemBorder => Line;
        public override System.Drawing.Color MenuItemSelected => Hover;
        public override System.Drawing.Color MenuItemSelectedGradientBegin => Hover;
        public override System.Drawing.Color MenuItemSelectedGradientEnd => Hover;
        public override System.Drawing.Color SeparatorDark => Line;
        public override System.Drawing.Color SeparatorLight => Bg;
    }
}
