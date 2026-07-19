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
        store.UpsertLocation(Place, T0, "Nyx Castra Jump Point");
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

        Assert.Equal(2, store.SchemaVersion);
        // The column now exists and is writable via UpsertContainer.
        store.UpsertContainer(Box, Place, T0, "Stor-All 2 SCU");
        using var q = conn.CreateCommand();
        q.CommandText = "SELECT parent_id FROM locations WHERE id = $id;";
        q.Parameters.AddWithValue("$id", Box);
        Assert.Equal(Place, Convert.ToInt64(q.ExecuteScalar()));
    }
}
