using AssetMemory.Collector;
using AssetMemory.Core.Detection;
using AssetMemory.Core.Inventory;
using AssetMemory.Core.Resolution;
using AssetMemory.Data;
using AssetMemory.Data.Events;
using AssetMemory.UI.Components;
using AssetMemory.UI.Services;
using Microsoft.Data.Sqlite;

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

var port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var p) ? p : 9222;
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

app.Run();
