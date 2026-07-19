using AssetMemory.Collector.Control;
using AssetMemory.Tui.Ui;
using Terminal.Gui.App;

namespace AssetMemory.Tui;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // Headless smoke tests for environments without an interactive terminal to drive the TUI.
        switch (Environment.GetEnvironmentVariable("ASSETMEMORY_TUI_SELFTEST"))
        {
            case "1": return SelfTestSole();      // sole-instance wiring: host + DB + query + action
            case "serve": return SelfTestServe(); // host + control pipe, held open briefly
            case "viewer": return SelfTestViewer(); // connect to a running pipe and round-trip
        }

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
                var client = new ControlPipeClient();
                ControlInfo info;
                try
                {
                    info = client.Info();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        "A background AssetMemory is running but its control pipe could not be reached " +
                        $"({ex.Message}). It may still be starting up — retry in a moment.");
                    return 1;
                }
                dbPath = info.DbPath;
                actions = new PipeActions(client, info.GameLogPath, info.Inception);
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

    private static int SelfTestSole()
    {
        using var host = new AppHost();
        using var read = new ReadStore(host.DbPath);

        var page = read.Store.GetHoldingDetailsPage(null, null, null, "item", true, 1, 25);
        var locations = read.Store.GetLocationsWithHoldings().ToList();
        Console.WriteLine($"SELFTEST ok: dbPath={host.DbPath}");
        Console.WriteLine($"SELFTEST rows={page.TotalCount} locations={locations.Count} " +
                          $"units={page.TotalUnits} gameLog={host.Actions.GameLogPath ?? "(none)"}");

        var bad = host.Actions.SetPath(Path.Combine(Path.GetTempPath(), "definitely-not-star-citizen"), false);
        Console.WriteLine($"SELFTEST setpath(bad).Ok={bad.Ok} err={bad.Error}");
        return 0;
    }

    // Boots the collector + control pipe and holds it open briefly, so a separate viewer process can
    // attach over the pipe. Exercises the TUI-as-server side of the fixed viewer-mode gap.
    private static int SelfTestServe()
    {
        using var host = new AppHost();
        Console.WriteLine($"SELFTEST serve: pipe up, dbPath={host.DbPath}. serving 8s...");
        Thread.Sleep(TimeSpan.FromSeconds(8));
        Console.WriteLine("SELFTEST serve: done");
        return 0;
    }

    // Connects to whatever is serving the control pipe (web host or a sole-instance TUI) and
    // round-trips a couple of requests. Exercises the viewer side.
    private static int SelfTestViewer()
    {
        var client = new ControlPipeClient();
        var info = client.Info();
        Console.WriteLine($"SELFTEST viewer: connected. dbPath={info.DbPath} gameLog={info.GameLogPath ?? "(none)"}");

        var bad = client.SetPath(Path.Combine(Path.GetTempPath(), "definitely-not-star-citizen"), false);
        Console.WriteLine($"SELFTEST viewer: setpath(bad).Ok={bad.Ok} err={bad.Error}");
        return 0;
    }
}
