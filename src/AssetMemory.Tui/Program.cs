using AssetMemory.Tui.Ui;
using Terminal.Gui.App;

namespace AssetMemory.Tui;

internal static class Program
{
    private const string WebBaseUrl = "http://localhost:9222";

    [STAThread]
    private static int Main()
    {
        // Headless smoke test of the sole-instance wiring (DI host + DB + query surface + an action),
        // for environments without an interactive terminal to drive the TUI. Renders nothing.
        if (Environment.GetEnvironmentVariable("ASSETMEMORY_TUI_SELFTEST") == "1")
            return SelfTest();

        // Decide mode via the same single-instance mutex the tray/web app uses. If a background
        // AssetMemory already holds it, we attach as a read-only viewer and delegate writes over
        // HTTP. Otherwise we own the collector ourselves (full standalone).
        using var mutex = new Mutex(initiallyOwned: false, @"Local\AssetMemory.SingleInstance", out _);
        bool backgroundRunning;
        try { backgroundRunning = !mutex.WaitOne(0); }
        catch (AbandonedMutexException) { backgroundRunning = false; }

        AppHost? host = null;
        var ownsMutex = !backgroundRunning;

        try
        {
            string dbPath;
            IActions actions;

            if (backgroundRunning)
            {
                HostInfo info;
                try
                {
                    info = HostInfo.Fetch(WebBaseUrl);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"A background AssetMemory is running but its control API at {WebBaseUrl} " +
                        $"could not be reached ({ex.Message}). Close it and retry, or open the web UI instead.");
                    return 1;
                }
                dbPath = info.DbPath;
                actions = new HttpActions(WebBaseUrl, info.GameLogPath);
            }
            else
            {
                host = new AppHost();
                dbPath = host.DbPath;
                actions = host.Actions;
            }

            using var read = new ReadStore(dbPath);

            Application.Init();
            try
            {
                // First-run: if no Game.log is configured yet, force setup before the main screen.
                if (string.IsNullOrEmpty(actions.GameLogPath) && !SetupDialog.Run(actions))
                    return 0; // user quit setup

                var win = new InventoryWindow(read.Store, actions);
                Application.Run(win);
                win.Dispose();
            }
            finally
            {
                Application.Shutdown();
            }

            return 0;
        }
        finally
        {
            host?.Dispose();
            if (ownsMutex)
            {
                try { mutex.ReleaseMutex(); } catch { /* never owned / already released */ }
            }
        }
    }

    private static int SelfTest()
    {
        using var host = new AppHost();
        using var read = new ReadStore(host.DbPath);

        var page = read.Store.GetHoldingDetailsPage(null, null, "item", true, 1, 25);
        var locations = read.Store.GetLocationsWithHoldings().ToList();
        Console.WriteLine($"SELFTEST ok: dbPath={host.DbPath}");
        Console.WriteLine($"SELFTEST rows={page.TotalCount} locations={locations.Count} " +
                          $"units={page.TotalUnits} gameLog={host.Actions.GameLogPath ?? "(none)"}");

        var bad = host.Actions.SetPath(Path.Combine(Path.GetTempPath(), "definitely-not-star-citizen"), false);
        Console.WriteLine($"SELFTEST setpath(bad).Ok={bad.Ok} err={bad.Error}");
        return 0;
    }
}
