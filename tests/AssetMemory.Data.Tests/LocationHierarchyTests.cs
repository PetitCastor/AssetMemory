using AssetMemory.Data;
using Microsoft.Data.Sqlite;

namespace AssetMemory.Data.Tests;

/// <summary>
/// Covers the place->container hierarchy added in schema v2: the parent_id column, the two
/// dropdown-backing queries, the "all locations" aggregate excluding container-held units, and the
/// in-place upgrade of a legacy v1 database.
/// </summary>
public class LocationHierarchyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 19, 0, 6, 0, TimeSpan.Zero);

    private const long Place = 141810852;   // a station (parent_id IS NULL)
    private const long Box = 681562156430;  // a Stor-All box sitting at Place
    private const long EmptyBox = 681562156431;

    // Place holds items directly AND has a stocked child box; an empty child box holds nothing.
    private static long Seed(AssetMemoryStore store)
    {
        store.UpsertLocation(Place, T0, "Nyx Castra Jump Point", "Nyx");
        store.UpsertContainer(Box, Place, T0, "Stor-All 2 SCU");
        store.UpsertContainer(EmptyBox, Place, T0, "Stor-All 8 SCU");

        var item = store.EnsureItem("behr_shotgun_ballistic_01", "BEHR Shotgun");
        store.AdjustHolding(Place, item, 5, T0);  // place-direct units
        store.AdjustHolding(Box, item, 3, T0);    // container units
        return item;
    }

    [Fact]
    public void GetPlacesWithHoldings_returns_the_place_and_never_a_container()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            Seed(store);
            var places = store.GetPlacesWithHoldings().ToList();
            Assert.Equal(Place, Assert.Single(places).Id);
        }
    }

    [Fact]
    public void GetPlacesWithHoldings_includes_a_place_stocked_only_through_a_child_box()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            // Place itself has no direct holdings; only its child box does.
            store.UpsertLocation(Place, T0, "Nyx Castra Jump Point");
            store.UpsertContainer(Box, Place, T0, "Stor-All 2 SCU");
            var item = store.EnsureItem("foo", "Foo");
            store.AdjustHolding(Box, item, 1, T0);

            Assert.Equal(Place, Assert.Single(store.GetPlacesWithHoldings()).Id);
        }
    }

    [Fact]
    public void GetPlacesWithHoldings_omits_a_labelled_place_with_no_holdings_anywhere()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            store.UpsertLocation(Place, T0, "Empty Place");
            Assert.Empty(store.GetPlacesWithHoldings());
        }
    }

    [Fact]
    public void GetContainersForPlace_returns_only_the_stocked_child_boxes()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            Seed(store);
            var boxes = store.GetContainersForPlace(Place).ToList();
            // The empty box is excluded (no holdings); the stocked box is returned.
            Assert.Equal(Box, Assert.Single(boxes).Id);
        }
    }

    [Fact]
    public void GetHoldingDetailsPage_all_locations_rolls_up_place_and_container_units()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            Seed(store);
            // "All locations" = everything: 5 place-direct + 3 in the box = 8, across 2 locations.
            var all = store.GetHoldingDetailsPage(null, null, null, "item", true, 1, 50);
            Assert.Equal(8, all.TotalUnits);
            Assert.Equal(2, all.DistinctLocations);
        }
    }

    [Fact]
    public void GetHoldingDetailsPage_place_scope_rolls_up_boxes_but_container_scope_narrows()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            Seed(store);
            // Place scope = its 5 direct units + the box's 3 = 8.
            Assert.Equal(8, store.GetHoldingDetailsPage(Place, null, null, "item", true, 1, 50).TotalUnits);
            // Container scope = only the box's 3, whether or not the place is also supplied.
            Assert.Equal(3, store.GetHoldingDetailsPage(Place, Box, null, "item", true, 1, 50).TotalUnits);
            Assert.Equal(3, store.GetHoldingDetailsPage(null, Box, null, "item", true, 1, 50).TotalUnits);
        }
    }

    [Fact]
    public void GetHoldingDetailsPage_leaves_LocationParentLabel_blank_for_place_direct_rows_and_set_for_container_rows()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            Seed(store);
            var rows = store.GetHoldingDetailsPage(null, null, null, "item", true, 1, 50).Rows;

            // Place-direct row: Location already shows the place, so Local Storage stays blank
            // (nothing to add) rather than repeating the same text.
            var placeRow = Assert.Single(rows, r => r.LocationId == Place);
            Assert.Null(placeRow.LocationParentLabel);

            // Container row: Location shows the box's (possibly ambiguous, reused) label, so
            // Local Storage carries the actual place it sits at.
            var boxRow = Assert.Single(rows, r => r.LocationId == Box);
            Assert.Equal("Nyx Castra Jump Point", boxRow.LocationParentLabel);
        }
    }

    [Fact]
    public void GetSystemsWithHoldings_returns_distinct_buckets_for_stocked_places_only()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            Seed(store); // Place is tagged "Nyx" and stocked
            store.UpsertLocation(999, T0, "Some Stanton Hub", "Stanton"); // stocked, different system
            store.UpsertLocation(998, T0, "Empty Pyro Outpost", "Pyro"); // no holdings anywhere

            var item = store.EnsureItem("widget", "Widget");
            store.AdjustHolding(999, item, 1, T0);

            var systems = store.GetSystemsWithHoldings().ToList();
            Assert.Contains("Nyx", systems);
            Assert.Contains("Stanton", systems);
            Assert.DoesNotContain("Pyro", systems); // stocked nowhere
        }
    }

    [Fact]
    public void GetSystemsWithHoldings_buckets_a_place_with_no_resolved_system_as_other()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            // A freestanding local-storage place never tagged with a system (system left null).
            store.UpsertLocation(Place, T0, "Stor-All 2 SCU");
            var item = store.EnsureItem("widget", "Widget");
            store.AdjustHolding(Place, item, 1, T0);

            Assert.Equal("Other", Assert.Single(store.GetSystemsWithHoldings()));
        }
    }

    [Fact]
    public void GetPlacesWithHoldings_narrows_to_the_given_system()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            Seed(store); // Place is tagged "Nyx"
            store.UpsertLocation(999, T0, "Some Stanton Hub", "Stanton");
            var item = store.EnsureItem("widget", "Widget");
            store.AdjustHolding(999, item, 1, T0);

            Assert.Equal(Place, Assert.Single(store.GetPlacesWithHoldings("Nyx")).Id);
            Assert.Equal(999, Assert.Single(store.GetPlacesWithHoldings("Stanton")).Id);
        }
    }

    [Fact]
    public void GetHoldingDetailsPage_system_scope_rolls_up_every_place_and_box_under_it()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            Seed(store); // Place ("Nyx"): 5 direct + 3 in Box = 8 units
            store.UpsertLocation(999, T0, "Some Stanton Hub", "Stanton");
            var item = store.EnsureItem("widget", "Widget");
            store.AdjustHolding(999, item, 4, T0);

            // System scope: only Nyx's 8 units, not Stanton's 4.
            var nyx = store.GetHoldingDetailsPage(null, null, null, "item", true, 1, 50, "Nyx");
            Assert.Equal(8, nyx.TotalUnits);

            var stanton = store.GetHoldingDetailsPage(null, null, null, "item", true, 1, 50, "Stanton");
            Assert.Equal(4, stanton.TotalUnits);

            // No system filter still rolls up everything.
            var all = store.GetHoldingDetailsPage(null, null, null, "item", true, 1, 50);
            Assert.Equal(12, all.TotalUnits);
        }
    }

    [Fact]
    public void GetHoldingDetailsPage_container_and_place_scope_take_precedence_over_system()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            Seed(store);
            // A container/place scope narrows correctly even when an unrelated system is also passed.
            Assert.Equal(3, store.GetHoldingDetailsPage(null, Box, null, "item", true, 1, 50, "Stanton").TotalUnits);
            Assert.Equal(8, store.GetHoldingDetailsPage(Place, null, null, "item", true, 1, 50, "Stanton").TotalUnits);
        }
    }

    // ---------- optional storage (includeStorage flag) ----------

    [Fact]
    public void GetHoldingDetailsPage_hides_container_units_when_storage_excluded()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            Seed(store); // Place: 5 direct + Box: 3
            // Storage hidden: only the 5 place-direct units; the box's 3 drop out.
            Assert.Equal(5, store.GetHoldingDetailsPage(null, null, null, "item", true, 1, 50, includeStorage: false).TotalUnits);
            Assert.Equal(5, store.GetHoldingDetailsPage(Place, null, null, "item", true, 1, 50, includeStorage: false).TotalUnits);
            // An explicit box drill-in still works even with storage otherwise hidden.
            Assert.Equal(3, store.GetHoldingDetailsPage(Place, Box, null, "item", true, 1, 50, includeStorage: false).TotalUnits);
            // Default (storage shown) still rolls up to 8.
            Assert.Equal(8, store.GetHoldingDetailsPage(null, null, null, "item", true, 1, 50).TotalUnits);
        }
    }

    [Fact]
    public void GetHoldingDetailsPage_search_is_global_across_boxes_and_dropped_ignoring_scope_and_storage_hide()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var item = Seed(store);                    // Place("Nyx"): 5 direct + Box: 3, item = BEHR Shotgun
            store.UpsertLocation(-1, T0, "Dropped", "Dropped");
            store.AdjustHolding(-1, item, 2, T0);      // 2 more of the same item, dropped

            // Search "BEHR" with storage hidden AND an unrelated system scope must still find the item
            // everywhere: place-direct (5) + box (3) + dropped (2) = 10 across 3 locations.
            var hit = store.GetHoldingDetailsPage(null, null, "BEHR", "item", true, 1, 50, system: "Stanton", includeStorage: false);
            Assert.Equal(10, hit.TotalUnits);
            Assert.Equal(3, hit.DistinctLocations);
        }
    }

    [Fact]
    public void GetPlacesWithHoldings_drops_a_storage_only_place_when_storage_excluded()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            // Place has NO direct holdings; only its child box does.
            store.UpsertLocation(Place, T0, "Nyx Castra Jump Point", "Nyx");
            store.UpsertContainer(Box, Place, T0, "Stor-All 2 SCU");
            var item = store.EnsureItem("foo", "Foo");
            store.AdjustHolding(Box, item, 1, T0);

            Assert.Equal(Place, Assert.Single(store.GetPlacesWithHoldings(includeStorage: true)).Id);
            Assert.Empty(store.GetPlacesWithHoldings(includeStorage: false));
        }
    }

    [Fact]
    public void GetSystemsWithHoldings_drops_a_storage_only_system_when_storage_excluded()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            store.UpsertLocation(Place, T0, "Nyx Castra Jump Point", "Nyx");
            store.UpsertContainer(Box, Place, T0, "Stor-All 2 SCU");
            store.UpsertLocation(999, T0, "Some Stanton Hub", "Stanton");
            var item = store.EnsureItem("foo", "Foo");
            store.AdjustHolding(Box, item, 1, T0);   // Nyx: boxed only
            store.AdjustHolding(999, item, 1, T0);   // Stanton: direct

            var shown = store.GetSystemsWithHoldings(includeStorage: true).ToList();
            Assert.Contains("Nyx", shown);
            Assert.Contains("Stanton", shown);

            var hidden = store.GetSystemsWithHoldings(includeStorage: false).ToList();
            Assert.DoesNotContain("Nyx", hidden);   // Nyx was storage-only
            Assert.Contains("Stanton", hidden);      // Stanton has direct holdings
        }
    }

    [Fact]
    public void HasStorageInScope_reflects_boxes_present_globally_by_system_and_by_place()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            Seed(store); // Place("Nyx") has a stocked Box
            store.UpsertLocation(999, T0, "Some Stanton Hub", "Stanton");
            var item = store.EnsureItem("widget", "Widget");
            store.AdjustHolding(999, item, 1, T0);   // Stanton place: direct-only

            Assert.True(store.HasStorageInScope(null, null));         // a box exists somewhere
            Assert.True(store.HasStorageInScope("Nyx", null));        // Nyx has the box
            Assert.False(store.HasStorageInScope("Stanton", null));   // Stanton is direct-only
            Assert.True(store.HasStorageInScope(null, Place));        // the place has a box
            Assert.False(store.HasStorageInScope(null, 999));         // that place has no box
        }
    }

    [Fact]
    public void ApplyMigration_upgrades_a_legacy_v1_locations_table_in_place()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();

        // A v1 database: locations without the parent_id column, user_version stamped 1.
        using (var c = conn.CreateCommand())
        {
            c.CommandText = """
                CREATE TABLE locations (
                    id            INTEGER PRIMARY KEY,
                    label         TEXT,
                    last_seen_utc TEXT NOT NULL
                );
                PRAGMA user_version = 1;
                """;
            c.ExecuteNonQuery();
        }

        var store = new AssetMemoryStore(conn);
        store.ApplyMigration();  // CREATE IF NOT EXISTS is a no-op on the existing table; ALTER adds the column

        Assert.Equal(3, store.SchemaVersion);
        // Both columns added since v1 now exist and are writable.
        store.UpsertContainer(Box, Place, T0, "Stor-All 2 SCU");
        store.UpsertLocation(Place, T0, "Nyx Castra Jump Point", "Nyx");
        using var q = conn.CreateCommand();
        q.CommandText = "SELECT parent_id FROM locations WHERE id = $id;";
        q.Parameters.AddWithValue("$id", Box);
        Assert.Equal(Place, Convert.ToInt64(q.ExecuteScalar()));

        using var qs = conn.CreateCommand();
        qs.CommandText = "SELECT system FROM locations WHERE id = $id;";
        qs.Parameters.AddWithValue("$id", Place);
        Assert.Equal("Nyx", Convert.ToString(qs.ExecuteScalar()));
    }
}
