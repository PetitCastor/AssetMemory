using AssetMemory.Collector;
using AssetMemory.Collector.Control;
using AssetMemory.Core.Detection;
using AssetMemory.Core.Resolution;
using AssetMemory.Data;
using AssetMemory.Data.Events;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AssetMemory.Tui;

/// <summary>
/// Sole-instance mode: builds and runs the collector pipeline in-process, mirroring the web host's
/// service wiring (<c>AssetMemory.UI/Program.cs</c>) minus everything web. The TUI's read store opens
/// a separate connection to the same <see cref="DbPath"/>.
/// </summary>
public sealed class AppHost : IDisposable
{
    private readonly IHost _host;

    public string DbPath { get; }
    public AppSettings Settings { get; }
    public LocalActions Actions { get; }

    public AppHost()
    {
        // User data (DB + settings) lives under %LOCALAPPDATA%\AssetMemory (same as the web host);
        // EnsureReady migrates a legacy next-to-exe install on first run.
        AppPaths.EnsureReady();
        var settingsPath = AppPaths.SettingsPath;
        var settings = AppSettings.Load(settingsPath);

        // Auto-detect Game.log if not configured (same as the web host).
        if (string.IsNullOrEmpty(settings.GameLogPath) || !GamePathFinder.IsValidGameLog(settings.GameLogPath))
        {
            var found = GamePathFinder.FindGameLogPaths();
            if (found.Count > 0)
            {
                settings.GameLogPath = found[0];
                settings.Save(settingsPath);
            }
        }

        var dbPath = AppPaths.DbPath;
        var connString = $"Data Source={dbPath}";

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders(); // keep the console clean under the TUI

        builder.Services.AddSingleton(_ =>
        {
            var conn = new SqliteConnection(connString);
            conn.Open();
            var store = new AssetMemoryStore(conn);
            store.ApplyMigration();
            return store;
        });
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(_ => new LogTailer(settings.GameLogPath ?? ""));
        builder.Services.AddSingleton<IItemNameResolver>(_ =>
            new ItemNameResolver(GameItemNames.LoadForGameLog(settings.GameLogPath)));
        builder.Services.AddSingleton<IStationNameResolver, StationNameResolver>();
        builder.Services.AddSingleton<ISystemNameResolver, SystemNameResolver>();
        // Backfills names global.ini has no key for, via api.star-citizen.wiki's exact class_name
        // search. One-shot per launch, runs in the background — never blocks startup.
        builder.Services.AddSingleton(_ => new ExternalItemNameClient());
        builder.Services.AddHostedService(sp => new ExternalItemNameBackfillService(
            sp.GetRequiredService<AssetMemoryStore>(),
            sp.GetRequiredService<IItemNameResolver>(),
            sp.GetRequiredService<ExternalItemNameClient>(),
            sp.GetRequiredService<ILogger<ExternalItemNameBackfillService>>()));
        builder.Services.AddSingleton(sp => new EventApplier(
            sp.GetRequiredService<AssetMemoryStore>(),
            sp.GetRequiredService<IItemNameResolver>(),
            sp.GetRequiredService<IStationNameResolver>(),
            sp.GetRequiredService<ISystemNameResolver>())
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

        // Control channel — so a second (viewer) TUI can attach to this instance and delegate writes.
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

        _host = builder.Build();

        // Force store creation (runs ApplyMigration) and backfill display names before the read
        // connection opens, so the schema is guaranteed present.
        var store = _host.Services.GetRequiredService<AssetMemoryStore>();
        var itemNames = _host.Services.GetRequiredService<IItemNameResolver>();
        foreach (var item in store.GetAllItems())
            store.EnsureItem(item.ClassName, itemNames.Resolve(item.ClassName));

        _host.Start();

        DbPath = dbPath;
        Settings = settings;
        Actions = new LocalActions(
            _host.Services.GetRequiredService<ControlService>(),
            _host.Services.GetRequiredService<GameLogCollector>());
    }

    public void Dispose()
    {
        _host.StopAsync().GetAwaiter().GetResult();
        _host.Dispose();
    }
}
