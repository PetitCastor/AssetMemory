using System.Diagnostics;
using AssetMemory.Collector;
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

        var baseDir = AppContext.BaseDirectory;
        var settingsPath = Path.Combine(baseDir, "settings.json");
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
        var dbPath = Path.Combine(baseDir, "assetmemory.db");
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
            sp.GetRequiredService<IStationNameResolver>()));
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

        // Local control endpoints — let the console TUI (viewer mode) delegate writes to this
        // collector-owning process so cross-process state stays consistent. Localhost only.
        app.MapGet("/api/info", () => Results.Json(new
        {
            dbPath,
            gameLogPath = settings.GameLogPath,
        }));

        app.MapPost("/api/sync", () =>
            Results.Json(app.Services.GetRequiredService<SyncService>().Sync())).DisableAntiforgery();

        app.MapPost("/api/clear", () =>
        {
            var col = app.Services.GetRequiredService<GameLogCollector>();
            var st = app.Services.GetRequiredService<AssetMemoryStore>();
            col.StartFresh(() => st.ClearAll());
            settings.ProcessedBackups.Clear();
            settings.Save(settingsPath);
            return Results.Ok();
        }).DisableAntiforgery();

        app.MapPost("/api/path", (SetPathRequest req) =>
        {
            var resolved = GamePathFinder.FindGameLogInFolder(req.Folder);
            if (resolved is null && GamePathFinder.IsValidGameLog(req.Folder))
                resolved = Path.GetFullPath(req.Folder);
            if (resolved is null)
                return Results.BadRequest(new { error = "Game.log not found at that location." });

            settings.GameLogPath = resolved;
            settings.Save(settingsPath);
            var tailer = app.Services.GetRequiredService<LogTailer>();
            tailer.SetPath(resolved);
            if (req.StartFresh)
                tailer.SeekToEnd();
            return Results.Json(new { path = resolved });
        }).DisableAntiforgery();

        // Start Kestrel without blocking, then hand this STA thread to the tray message loop.
        // The collector runs as a hosted background service, so capture continues while the tray
        // sits idle. ASSETMEMORY_NO_BROWSER lets tests/headless launches skip the auto-open.
        app.Start();
        if (Environment.GetEnvironmentVariable("ASSETMEMORY_NO_BROWSER") is null)
            OpenBrowser(url);

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

/// <summary>Body for <c>POST /api/path</c> — the folder to search for Game.log, plus whether to skip history.</summary>
internal sealed record SetPathRequest(string Folder, bool StartFresh);
