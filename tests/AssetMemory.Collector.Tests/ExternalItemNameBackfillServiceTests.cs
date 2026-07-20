using System.Net;
using System.Net.Http;
using AssetMemory.Collector;
using AssetMemory.Core.Resolution;
using AssetMemory.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetMemory.Collector.Tests;

public class ExternalItemNameBackfillServiceTests
{
    private static (AssetMemoryStore store, SqliteConnection conn) NewStore()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var store = new AssetMemoryStore(conn);
        store.ApplyMigration();
        return (store, conn);
    }

    private sealed class FakeItemNameResolver : IItemNameResolver
    {
        private readonly HashSet<string> _overrides;

        public FakeItemNameResolver(params string[] overrides)
            => _overrides = new HashSet<string>(overrides, StringComparer.Ordinal);

        public string Resolve(string? itemClass) => itemClass ?? "";

        public bool HasOverride(string itemClass) => _overrides.Contains(itemClass);
    }

    /// <summary>Counts requests and maps the requested class_name to a canned resolution (or a 404).</summary>
    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly Func<string, string?> _resolve;

        public CountingHandler(Func<string, string?> resolve) => _resolve = resolve;

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var itemClass = Uri.UnescapeDataString(request.RequestUri!.Segments[^1]);
            var name = _resolve(itemClass);
            var response = name is not null
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"data\":{\"name\":\"" + name + "\"}}"),
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("""{"message":"No matching entity found."}"""),
                };
            return Task.FromResult(response);
        }
    }

    private static async Task RunToCompletionAsync(ExternalItemNameBackfillService service)
    {
        await service.StartAsync(CancellationToken.None);
        await (service.ExecuteTask ?? Task.CompletedTask);
    }

    [Fact]
    public async Task Items_global_ini_already_resolved_are_skipped_and_never_hit_the_client()
    {
        var (store, conn) = NewStore();
        using (conn)
        {
            store.EnsureItem("behr_smg_ballistic_01", "P8-SC SMG");
            var resolver = new FakeItemNameResolver("behr_smg_ballistic_01");
            var handler = new CountingHandler(_ => "should not be called");
            using var client = new ExternalItemNameClient(handler);
            var service = new ExternalItemNameBackfillService(
                store, resolver, client, NullLogger<ExternalItemNameBackfillService>.Instance);

            await RunToCompletionAsync(service);

            Assert.Equal(0, handler.RequestCount);
            Assert.Equal("P8-SC SMG", store.GetItem("behr_smg_ballistic_01")!.DisplayName);
        }
    }

    [Fact]
    public async Task Items_the_client_resolves_get_their_display_name_backfilled_in_the_store()
    {
        var (store, conn) = NewStore();
        using (conn)
        {
            const string className = "grin_multitool_resource_salvage_repair_01_filled";
            store.EnsureItem(className, "Grin multitool resource salvage repair 01 filled");
            var resolver = new FakeItemNameResolver(); // no ini overrides
            var handler = new CountingHandler(cn => cn == className ? "Cambio-Lite SRT Canister" : null);
            using var client = new ExternalItemNameClient(handler);
            var service = new ExternalItemNameBackfillService(
                store, resolver, client, NullLogger<ExternalItemNameBackfillService>.Instance);

            await RunToCompletionAsync(service);

            Assert.Equal(1, handler.RequestCount);
            Assert.Equal("Cambio-Lite SRT Canister", store.GetItem(className)!.DisplayName);
        }
    }

    [Fact]
    public async Task Resolved_names_are_cached_durably_and_survive_a_ClearAll_rebuild()
    {
        var (store, conn) = NewStore();
        using (conn)
        {
            const string className = "grin_multitool_resource_salvage_repair_01_filled";
            store.EnsureItem(className, "Grin multitool resource salvage repair 01 filled");
            var resolver = new FakeItemNameResolver();
            var handler = new CountingHandler(cn => cn == className ? "Cambio-Lite SRT Canister" : null);
            using var client = new ExternalItemNameClient(handler);
            var service = new ExternalItemNameBackfillService(
                store, resolver, client, NullLogger<ExternalItemNameBackfillService>.Instance);

            await RunToCompletionAsync(service);

            // Simulates "Start fresh" / a sync-inception date change: items gets wiped and rebuilt
            // from the log, which only knows the heuristic name -- the cached external name must win.
            store.ClearAll();
            store.EnsureItem(className, "Grin multitool resource salvage repair 01 filled");

            Assert.Equal("Cambio-Lite SRT Canister", store.GetItem(className)!.DisplayName);
        }
    }

    [Fact]
    public async Task Items_the_client_cannot_resolve_keep_their_existing_heuristic_name()
    {
        var (store, conn) = NewStore();
        using (conn)
        {
            store.EnsureItem("totally_unknown_class", "Totally Unknown Class");
            var resolver = new FakeItemNameResolver();
            var handler = new CountingHandler(_ => null);
            using var client = new ExternalItemNameClient(handler);
            var service = new ExternalItemNameBackfillService(
                store, resolver, client, NullLogger<ExternalItemNameBackfillService>.Instance);

            await RunToCompletionAsync(service);

            Assert.Equal(1, handler.RequestCount);
            Assert.Equal("Totally Unknown Class", store.GetItem("totally_unknown_class")!.DisplayName);
        }
    }
}
