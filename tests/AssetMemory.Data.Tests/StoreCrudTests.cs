using AssetMemory.Data;

namespace AssetMemory.Data.Tests;

public class StoreCrudTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 6, 24, 19, 22, 0, TimeSpan.Zero);

    // ---------- locations ----------

    [Fact]
    public void UpsertLocation_inserts_a_new_row()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            store.UpsertLocation(2900774186, T0, label: null);

            var loc = store.GetLocation(2900774186);
            Assert.NotNull(loc);
            Assert.Equal(2900774186, loc!.Id);
            Assert.Null(loc.Label);
            Assert.Equal(T0, loc.LastSeenUtc);
        }
    }

    [Fact]
    public void UpsertLocation_updates_last_seen_when_called_again()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var t1 = T0.AddMinutes(5);
            store.UpsertLocation(1, T0, null);
            store.UpsertLocation(1, t1, null);

            Assert.Equal(t1, store.GetLocation(1)!.LastSeenUtc);
        }
    }

    [Fact]
    public void UpsertLocation_does_not_overwrite_existing_label_with_null()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            store.UpsertLocation(1, T0, "Aaron Halo");
            store.UpsertLocation(1, T0.AddMinutes(1), label: null);

            Assert.Equal("Aaron Halo", store.GetLocation(1)!.Label);
        }
    }

    [Fact]
    public void UpsertLocation_replaces_label_when_a_non_null_one_is_supplied()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            store.UpsertLocation(1, T0, "old");
            store.UpsertLocation(1, T0.AddMinutes(1), "new");

            Assert.Equal("new", store.GetLocation(1)!.Label);
        }
    }

    // ---------- items ----------

    [Fact]
    public void EnsureItem_inserts_new_row_and_returns_id()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var id = store.EnsureItem("Drink_bottle_synergy_01_plus_a", "Synergy+ Bottle");
            Assert.True(id > 0);
        }
    }

    [Fact]
    public void EnsureItem_returns_same_id_for_same_class()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var a = store.EnsureItem("foo_bar", "Foo");
            var b = store.EnsureItem("foo_bar", "Foo (renamed)");
            Assert.Equal(a, b);
        }
    }

    [Fact]
    public void EnsureItem_updates_display_name_on_repeat()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            store.EnsureItem("foo", "Old");
            store.EnsureItem("foo", "New");
            Assert.Equal("New", store.GetItem("foo")!.DisplayName);
        }
    }

    // ---------- external item name cache ----------

    [Fact]
    public void EnsureItem_prefers_a_cached_external_name_over_the_freshly_computed_one_on_first_insert()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            store.UpsertExternalItemName("grin_multitool_01", "Cambio-Lite SRT Canister");

            store.EnsureItem("grin_multitool_01", "Grin multitool 01 salvage repair"); // heuristic guess

            Assert.Equal("Cambio-Lite SRT Canister", store.GetItem("grin_multitool_01")!.DisplayName);
        }
    }

    [Fact]
    public void EnsureItem_reapplies_the_cached_external_name_after_a_rebuild_wipes_the_item_row()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            store.EnsureItem("grin_multitool_01", "Grin multitool 01 salvage repair");
            store.UpsertExternalItemName("grin_multitool_01", "Cambio-Lite SRT Canister");
            store.EnsureItem("grin_multitool_01", "Cambio-Lite SRT Canister"); // as the backfill service would

            // Start fresh / a sync-inception date change wipes and rebuilds items from the log —
            // the rebuild only knows the heuristic guess, same as the very first ingest.
            store.ClearAll();
            store.EnsureItem("grin_multitool_01", "Grin multitool 01 salvage repair");

            Assert.Equal("Cambio-Lite SRT Canister", store.GetItem("grin_multitool_01")!.DisplayName);
        }
    }
}
