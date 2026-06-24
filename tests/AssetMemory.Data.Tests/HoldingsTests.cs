using AssetMemory.Data;

namespace AssetMemory.Data.Tests;

public class HoldingsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 24, 19, 22, 0, TimeSpan.Zero);

    [Fact]
    public void Adjust_inserts_a_new_holding_row()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            store.UpsertLocation(1, T0, null);
            var item = store.EnsureItem("foo", "Foo");

            store.AdjustHolding(locationId: 1, itemId: item, delta: 3, atUtc: T0);

            var h = store.GetHolding(1, item);
            Assert.NotNull(h);
            Assert.Equal(3, h!.Quantity);
            Assert.Equal(T0, h.FirstSeenUtc);
            Assert.Equal(T0, h.LastSeenUtc);
        }
    }

    [Fact]
    public void Adjust_increments_existing_holding_and_updates_last_seen()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            store.UpsertLocation(1, T0, null);
            var item = store.EnsureItem("foo", "Foo");

            var t1 = T0.AddSeconds(10);
            store.AdjustHolding(1, item, +2, T0);
            store.AdjustHolding(1, item, +5, t1);

            var h = store.GetHolding(1, item)!;
            Assert.Equal(7, h.Quantity);
            Assert.Equal(T0, h.FirstSeenUtc);
            Assert.Equal(t1, h.LastSeenUtc);
        }
    }

    [Fact]
    public void Adjust_with_negative_delta_decrements_holding()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            store.UpsertLocation(1, T0, null);
            var item = store.EnsureItem("foo", "Foo");

            store.AdjustHolding(1, item, +5, T0);
            store.AdjustHolding(1, item, -2, T0.AddSeconds(1));

            Assert.Equal(3, store.GetHolding(1, item)!.Quantity);
        }
    }

    [Fact]
    public void Adjust_deletes_holding_when_quantity_reaches_zero()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            store.UpsertLocation(1, T0, null);
            var item = store.EnsureItem("foo", "Foo");

            store.AdjustHolding(1, item, +2, T0);
            store.AdjustHolding(1, item, -2, T0.AddSeconds(1));

            Assert.Null(store.GetHolding(1, item));
        }
    }

    [Fact]
    public void Adjust_clamps_at_zero_and_deletes_when_delta_overshoots()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            store.UpsertLocation(1, T0, null);
            var item = store.EnsureItem("foo", "Foo");

            store.AdjustHolding(1, item, +2, T0);
            store.AdjustHolding(1, item, -5, T0.AddSeconds(1));

            Assert.Null(store.GetHolding(1, item));
        }
    }

    [Fact]
    public void GetHoldingsForLocation_returns_all_items_with_quantity()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            store.UpsertLocation(1, T0, "Hangar");
            var a = store.EnsureItem("a", "A");
            var b = store.EnsureItem("b", "B");
            store.AdjustHolding(1, a, 3, T0);
            store.AdjustHolding(1, b, 1, T0);

            var holdings = store.GetHoldingsForLocation(1).ToList();
            Assert.Equal(2, holdings.Count);
            Assert.Contains(holdings, h => h.ItemId == a && h.Quantity == 3);
            Assert.Contains(holdings, h => h.ItemId == b && h.Quantity == 1);
        }
    }

    [Fact]
    public void FindLocationsHoldingItem_returns_locations_with_positive_quantity_only()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            store.UpsertLocation(1, T0, "Hangar");
            store.UpsertLocation(2, T0, "Ship");
            store.UpsertLocation(3, T0, "Outpost");
            var item = store.EnsureItem("foo", "Foo");

            store.AdjustHolding(1, item, 2, T0);
            store.AdjustHolding(2, item, 5, T0);
            store.AdjustHolding(3, item, 1, T0);
            store.AdjustHolding(3, item, -1, T0.AddSeconds(1)); // drops to 0 -> removed

            var locations = store.FindLocationsHoldingItem(item).ToList();
            Assert.Equal(2, locations.Count);
            Assert.Contains(locations, l => l.LocationId == 1 && l.Quantity == 2);
            Assert.Contains(locations, l => l.LocationId == 2 && l.Quantity == 5);
        }
    }
}
