using AssetMemory.Data;

namespace AssetMemory.Data.Tests;

public class QueryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 24, 19, 22, 0, TimeSpan.Zero);

    private static (AssetMemoryStore store, Microsoft.Data.Sqlite.SqliteConnection conn) Seeded()
    {
        var (store, conn) = TestStore.CreateMigrated();

        store.UpsertLocation(100, T0, "Hangar A");
        store.UpsertLocation(200, T0, "Ship Cargo");
        store.UpsertLocation(300, T0, null); // unlabelled

        var synergy = store.EnsureItem("Drink_bottle_synergy_01_plus_a", "Synergy+ Bottle");
        var medpen = store.EnsureItem("medpen_tier1", "MedPen");
        var helmet = store.EnsureItem("helmet_x", "Helmet X");

        store.AdjustHolding(100, synergy, 5, T0);
        store.AdjustHolding(100, medpen, 2, T0);
        store.AdjustHolding(200, synergy, 3, T0);
        store.AdjustHolding(300, helmet, 1, T0);

        return (store, conn);
    }

    // ---------- GetAllHoldingDetails ----------

    [Fact]
    public void GetAllHoldingDetails_returns_joined_rows_with_item_and_location_info()
    {
        var (store, conn) = Seeded();
        using (conn)
        {
            var all = store.GetAllHoldingDetails().ToList();

            Assert.Equal(4, all.Count);

            var synergyInHangar = all.Single(h => h.ItemClassName == "Drink_bottle_synergy_01_plus_a" && h.LocationId == 100);
            Assert.Equal("Synergy+ Bottle", synergyInHangar.ItemDisplayName);
            Assert.Equal("Hangar A", synergyInHangar.LocationLabel);
            Assert.Equal(5, synergyInHangar.Quantity);
        }
    }

    [Fact]
    public void GetAllHoldingDetails_returns_null_label_for_unlabelled_locations()
    {
        var (store, conn) = Seeded();
        using (conn)
        {
            var all = store.GetAllHoldingDetails().ToList();
            var helmetRow = all.Single(h => h.ItemClassName == "helmet_x");
            Assert.Null(helmetRow.LocationLabel);
            Assert.Equal(300, helmetRow.LocationId);
        }
    }

    [Fact]
    public void GetAllHoldingDetails_returns_empty_when_no_holdings_exist()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            Assert.Empty(store.GetAllHoldingDetails());
        }
    }

    // ---------- SearchItems ----------

    [Fact]
    public void SearchItems_matches_display_name_case_insensitively()
    {
        var (store, conn) = Seeded();
        using (conn)
        {
            var results = store.SearchItems("synergy").ToList();
            Assert.Single(results);
            Assert.Equal("Drink_bottle_synergy_01_plus_a", results[0].ClassName);
        }
    }

    [Fact]
    public void SearchItems_matches_class_name()
    {
        var (store, conn) = Seeded();
        using (conn)
        {
            var results = store.SearchItems("medpen").ToList();
            Assert.Single(results);
            Assert.Equal("MedPen", results[0].DisplayName);
        }
    }

    [Fact]
    public void SearchItems_returns_empty_for_no_match()
    {
        var (store, conn) = Seeded();
        using (conn)
        {
            Assert.Empty(store.SearchItems("nonexistent"));
        }
    }

    [Fact]
    public void SearchItems_matches_partial_substrings()
    {
        var (store, conn) = Seeded();
        using (conn)
        {
            var results = store.SearchItems("bottle").ToList();
            Assert.Single(results);
            Assert.Equal("Synergy+ Bottle", results[0].DisplayName);
        }
    }

    [Fact]
    public void SearchItems_returns_multiple_matches_when_term_matches_several()
    {
        var (store, conn) = Seeded();
        using (conn)
        {
            // "e" appears in synergy, medpen, and helmet
            var results = store.SearchItems("e").ToList();
            Assert.Equal(3, results.Count);
        }
    }

    [Fact]
    public void SearchItems_only_returns_items_that_have_holdings()
    {
        var (store, conn) = Seeded();
        using (conn)
        {
            // orphan item with no holdings
            store.EnsureItem("orphan_item", "Orphan");
            var results = store.SearchItems("orphan").ToList();
            Assert.Empty(results);
        }
    }

    // ---------- GetItemLocationDetails ----------

    [Fact]
    public void GetItemLocationDetails_returns_locations_holding_that_item_with_labels()
    {
        var (store, conn) = Seeded();
        using (conn)
        {
            var synergy = store.GetItem("Drink_bottle_synergy_01_plus_a")!;
            var locations = store.GetItemLocationDetails(synergy.Id).ToList();

            Assert.Equal(2, locations.Count);
            Assert.Contains(locations, l => l.LocationId == 100 && l.LocationLabel == "Hangar A" && l.Quantity == 5);
            Assert.Contains(locations, l => l.LocationId == 200 && l.LocationLabel == "Ship Cargo" && l.Quantity == 3);
        }
    }

    [Fact]
    public void GetItemLocationDetails_returns_empty_for_item_with_no_holdings()
    {
        var (store, conn) = Seeded();
        using (conn)
        {
            var orphanId = store.EnsureItem("orphan", null);
            Assert.Empty(store.GetItemLocationDetails(orphanId));
        }
    }

    [Fact]
    public void GetItemLocationDetails_includes_unlabelled_locations()
    {
        var (store, conn) = Seeded();
        using (conn)
        {
            var helmet = store.GetItem("helmet_x")!;
            var locations = store.GetItemLocationDetails(helmet.Id).ToList();
            Assert.Single(locations);
            Assert.Null(locations[0].LocationLabel);
        }
    }

    // ---------- GetEquippedDetails ----------

    [Fact]
    public void GetEquippedDetails_returns_joined_loadout_with_item_names()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var itemId = store.EnsureItem("helmet_x", "Helmet X");
            store.UpsertEquipped("Arcadiius", "Armor_Helmet", itemId, 999, "helmet_x_1", "persistent", T0);

            var loadout = store.GetEquippedDetails("Arcadiius").ToList();
            Assert.Single(loadout);
            Assert.Equal("Armor_Helmet", loadout[0].Port);
            Assert.Equal("Helmet X", loadout[0].ItemDisplayName);
            Assert.Equal("helmet_x", loadout[0].ItemClassName);
        }
    }

    [Fact]
    public void GetEquippedDetails_returns_empty_for_unknown_player()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            Assert.Empty(store.GetEquippedDetails("nobody"));
        }
    }
}
