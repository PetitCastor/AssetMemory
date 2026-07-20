using AssetMemory.Core.Resolution;
using AssetMemory.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AssetMemory.Collector;

/// <summary>
/// One-shot startup sweep that fills in display names global.ini can't resolve, via
/// api.star-citizen.wiki's exact <c>class_name</c> search. Runs once per app launch over items
/// already captured; new items captured mid-session get their heuristic name immediately and are
/// picked up by a later restart's sweep if still unresolved — the same startup-only limitation the
/// existing ini backfill already has.
/// </summary>
public sealed class ExternalItemNameBackfillService : BackgroundService
{
    private readonly AssetMemoryStore _store;
    private readonly IItemNameResolver _resolver;
    private readonly ExternalItemNameClient _client;
    private readonly ILogger<ExternalItemNameBackfillService> _logger;

    public ExternalItemNameBackfillService(
        AssetMemoryStore store,
        IItemNameResolver resolver,
        ExternalItemNameClient client,
        ILogger<ExternalItemNameBackfillService> logger)
    {
        _store = store;
        _resolver = resolver;
        _client = client;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var item in _store.GetAllItems())
        {
            if (stoppingToken.IsCancellationRequested) break;
            if (_resolver.HasOverride(item.ClassName)) continue; // global.ini already has it

            var name = await _client.TryResolveAsync(item.ClassName, stoppingToken);
            if (name is not null)
            {
                // Cache first so the name survives a later ClearAll (Start fresh / sync-inception
                // rebuild) instead of reverting to the heuristic guess until the next app restart.
                _store.UpsertExternalItemName(item.ClassName, name);
                _store.EnsureItem(item.ClassName, name);
                _logger.LogInformation("Resolved {ClassName} -> {Name} via star-citizen.wiki", item.ClassName, name);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); // stay well under 60 req/min
        }
    }
}
