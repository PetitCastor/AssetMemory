using System.Diagnostics;
using AssetMemory.Collector;
using AssetMemory.Collector.Control;
using AssetMemory.Core.Detection;
using AssetMemory.Core.Resolution;
using AssetMemory.Data;
using AssetMemory.Data.Events;
using AssetMemory.UI.Components;
using Microsoft.Data.Sqlite;

namespace AssetMemory.UI;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var p) ? p : 9222;
        var url = $"http://localhost:{port}";

        // Single instance: a second launch (e.g. double-clicking the exe again) just surfaces the
        // already-running UI instead of crashing on a port collision.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\AssetMemory.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            OpenBrowser(url);
            return;
        }

        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // User data (DB + settings) lives under %LOCALAPPDATA%\AssetMemory so it survives redeploys;
        // EnsureReady migrates a legacy next-to-exe install on first run.
        AppPaths.EnsureReady();
        var settingsPath = AppPaths.SettingsPath;
        var settings = AppSettings.Load(settingsPath);

        // Auto-detect Game.log if not configured
        if (string.IsNullOrEmpty(settings.GameLogPath) || !GamePathFinder.IsValidGameLog(settings.GameLogPath))
        {
            var found = GamePathFinder.FindGameLogPaths();
            if (found.Count > 0)
            {
                settings.GameLogPath = found[0];
                settings.Save(settingsPath);
            }
        }

        // SQLite store (singleton — WAL handles concurrent reads)
        var dbPath = AppPaths.DbPath;
        var connString = $"Data Source={dbPath}";

        builder.Services.AddSingleton(_ =>
        {
            var conn = new SqliteConnection(connString);
            conn.Open();
            var store = new AssetMemoryStore(conn);
            store.ApplyMigration();
            return store;
        });

        builder.Services.AddSingleton(settings);

        // Collector pipeline — always registered; LogTailer gracefully no-ops when path is empty
        builder.Services.AddSingleton(_ => new LogTailer(settings.GameLogPath ?? ""));
        // Real item display names come from the game's loose localization file (global.ini) next to
        // the configured Game.log; falls back to the heuristic formatter when it can't be found.
        builder.Services.AddSingleton<IItemNameResolver>(_ =>
            new ItemNameResolver(GameItemNames.LoadForGameLog(settings.GameLogPath)));
        builder.Services.AddSingleton<IStationNameResolver, StationNameResolver>();
        builder.Services.AddSingleton(sp => new EventApplier(
            sp.GetRequiredService<AssetMemoryStore>(),
            sp.GetRequiredService<IItemNameResolver>(),
            sp.GetRequiredService<IStationNameResolver>())
        {
            InceptionUtc = settings.SyncInceptionUtc,
        });
        builder.Services.AddSingleton(sp => new GameLogCollector(
            sp.GetRequiredService<LogTailer>(),
            sp.GetRequiredService<EventApplier>()));
        builder.Services.AddHostedService(sp => new CollectorService(
            sp.GetRequiredService<GameLogCollector>(),
            sp.GetRequiredService<ILogger<CollectorService>>()));
        builder.Services.AddSingleton(sp => new SyncService(
            sp.GetRequiredService<GameLogCollector>(),
            settings,
            settingsPath,
            sp.GetRequiredService<ILogger<SyncService>>()));

        // Control channel — lets console-TUI viewers delegate writes to this collector-owning
        // process over a local named pipe, keeping the collector's in-memory state consistent.
        builder.Services.AddSingleton(sp => new ControlService(
            sp.GetRequiredService<GameLogCollector>(),
            sp.GetRequiredService<SyncService>(),
            sp.GetRequiredService<AssetMemoryStore>(),
            sp.GetRequiredService<LogTailer>(),
            sp.GetRequiredService<EventApplier>(),
            settings,
            settingsPath,
            dbPath));
        builder.Services.AddHostedService(sp => new ControlPipeServer(
            sp.GetRequiredService<ControlService>(),
            sp.GetRequiredService<ILogger<ControlPipeServer>>()));

        builder.WebHost.ConfigureKestrel(k =>
        {
            k.ListenLocalhost(port);
        });

        var app = builder.Build();

        // Eagerly initialize the store, then backfill display names for items already captured before
        // the localization map was wired in. Runs before the collector starts, so no connection race.
        var store = app.Services.GetRequiredService<AssetMemoryStore>();
        var itemNames = app.Services.GetRequiredService<IItemNameResolver>();
        foreach (var item in store.GetAllItems())
            store.EnsureItem(item.ClassName, itemNames.Resolve(item.ClassName));

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
        }

        app.UseAntiforgery();
        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        // Start Kestrel without blocking, then hand this STA thread to the tray message loop.
        // The collector runs as a hosted background service, so capture continues while the tray
        // sits idle. No auto-opened browser: the app starts hidden in the tray, like a background
        // service, until the user picks "Open AssetMemory" or double-clicks the icon.
        app.Start();

        TrayApp.Run(url); // blocks until the user picks Exit from the tray menu

        app.StopAsync().GetAwaiter().GetResult();
    }

    internal static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // No default browser / locked-down shell — the URL is still reachable manually.
        }
    }
}
