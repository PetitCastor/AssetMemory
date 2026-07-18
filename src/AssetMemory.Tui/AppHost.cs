using AssetMemory.Collector;
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
        var baseDir = AppContext.BaseDirectory;
        var settingsPath = Path.Combine(baseDir, "settings.json");
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

        var dbPath = Path.Combine(baseDir, "assetmemory.db");
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
            _host.Services.GetRequiredService<GameLogCollector>(),
            _host.Services.GetRequiredService<SyncService>(),
            store,
            _host.Services.GetRequiredService<LogTailer>(),
            settings,
            settingsPath);
    }

    public void Dispose()
    {
        _host.StopAsync().GetAwaiter().GetResult();
        _host.Dispose();
    }
}
