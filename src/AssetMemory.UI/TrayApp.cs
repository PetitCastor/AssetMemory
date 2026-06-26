using System.Drawing;
using System.Windows.Forms;

namespace AssetMemory.UI;

/// <summary>
/// The system-tray presence for the app: a <see cref="NotifyIcon"/> with an "Open"/"Exit" menu.
/// <see cref="Run"/> owns the WinForms message loop and blocks the calling (STA) thread until the
/// user chooses Exit, at which point control returns to <see cref="Program"/> to stop the web host.
/// </summary>
internal static class TrayApp
{
    public static void Run(string url)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open AssetMemory", null, (_, _) => Program.OpenBrowser(url));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Application.Exit());

        using var icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "AssetMemory — Star Citizen inventory",
            Visible = true,
            ContextMenuStrip = menu,
        };
        icon.DoubleClick += (_, _) => Program.OpenBrowser(url);

        icon.BalloonTipTitle = "AssetMemory is running";
        icon.BalloonTipText = "Tracking your Star Citizen inventory. Double-click the tray icon to open.";
        icon.ShowBalloonTip(3000);

        Application.Run(); // pumps tray messages until Application.Exit()

        icon.Visible = false; // pull the icon out of the tray immediately on exit
    }

    /// <summary>Ships-with custom icon (<c>app.ico</c> next to the exe) if present, else the stock Windows app icon.</summary>
    private static Icon LoadIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "app.ico");
        return File.Exists(path) ? new Icon(path) : SystemIcons.Application;
    }
}
