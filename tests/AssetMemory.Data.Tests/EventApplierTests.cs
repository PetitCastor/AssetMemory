using AssetMemory.Core.Inventory;
using AssetMemory.Core.Inventory.Events;
using AssetMemory.Core.Resolution;
using AssetMemory.Data;
using AssetMemory.Data.Events;

namespace AssetMemory.Data.Tests;

public class EventApplierTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 24, 19, 22, 0, TimeSpan.Zero);

    private static EventApplier ApplierFor(AssetMemoryStore store, IItemNameResolver? names = null)
        => new(store, names ?? new ItemNameResolver());

    // ---------- single-event behaviour ----------

    [Fact]
    public void Move_credits_target_and_debits_source()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var applier = ApplierFor(store);
            applier.Apply(new ItemMovedEvent(
                T0,
                Player: "Arcadiius",
                ItemClass: "Drink_bottle_synergy_01_plus_a",
                Quantity: 2,
                Source: new InventoryRef(11, InventoryKind.Container, 0, "11:Container:0"),
                Target: new InventoryRef(22, InventoryKind.Container, 0, "22:Container:0"),
                RequestId: 1));

            var item = store.GetItem("Drink_bottle_synergy_01_plus_a")!;
            Assert.Null(store.GetHolding(11, item.Id));   // debited to zero -> removed
            var target = store.GetHolding(22, item.Id)!;
            Assert.Equal(2, target.Quantity);
        }
    }

    [Fact]
    public void Move_round_trip_with_unknown_source_ends_with_items_back_in_source()
    {
        // The ledger discovers where items LIVE from moves. Starting from zero knowledge,
        // taking items out of A then putting them back correctly ends with A holding them.
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var applier = ApplierFor(store);
            applier.Apply(new ItemMovedEvent(T0,
                "Arcadiius", "foo", 2,
                new InventoryRef(11, InventoryKind.Container, 0, "11:Container:0"),
                new InventoryRef(22, InventoryKind.Container, 0, "22:Container:0"),
                1));
            applier.Apply(new ItemMovedEvent(T0.AddSeconds(1),
                "Arcadiius", "foo", 2,
                new InventoryRef(22, InventoryKind.Container, 0, "22:Container:0"),
                new InventoryRef(11, InventoryKind.Container, 0, "11:Container:0"),
                2));

            var item = store.GetItem("foo")!;
            Assert.Equal(2, store.GetHolding(11, item.Id)!.Quantity);
            Assert.Null(store.GetHolding(22, item.Id));
        }
    }

    [Fact]
    public void Move_round_trip_with_seeded_source_restores_original_quantities()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            store.UpsertLocation(11, T0, null);
            store.UpsertLocation(22, T0, null);
            var item = store.EnsureItem("foo", "Foo");
            store.AdjustHolding(11, item, 5, T0);

            var applier = ApplierFor(store);
            applier.Apply(new ItemMovedEvent(T0.AddSeconds(1),
                "Arcadiius", "foo", 2,
                new InventoryRef(11, InventoryKind.Container, 0, "11:Container:0"),
                new InventoryRef(22, InventoryKind.Container, 0, "22:Container:0"),
                1));
            applier.Apply(new ItemMovedEvent(T0.AddSeconds(2),
                "Arcadiius", "foo", 2,
                new InventoryRef(22, InventoryKind.Container, 0, "22:Container:0"),
                new InventoryRef(11, InventoryKind.Container, 0, "11:Container:0"),
                2));

            Assert.Equal(5, store.GetHolding(11, item)!.Quantity);
            Assert.Null(store.GetHolding(22, item));
        }
    }

    [Fact]
    public void Move_creates_item_using_resolver_display_name()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var names = new ItemNameResolver(new Dictionary<string, string>
            {
                ["Drink_bottle_synergy_01_plus_a"] = "Synergy+ Bottle",
            });
            ApplierFor(store, names).Apply(new ItemMovedEvent(T0,
                "Arcadiius", "Drink_bottle_synergy_01_plus_a", 2,
                new InventoryRef(11, InventoryKind.Container, 0, "11:Container:0"),
                new InventoryRef(22, InventoryKind.Container, 0, "22:Container:0"),
                1));

            Assert.Equal("Synergy+ Bottle",
                store.GetItem("Drink_bottle_synergy_01_plus_a")!.DisplayName);
        }
    }

    [Fact]
    public void ContainerOpened_creates_location_row_without_a_label()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            ApplierFor(store).Apply(new ContainerOpenedEvent(
                T0, "Arcadiius",
                new InventoryRef(0, InventoryKind.Location, 2900774186, "0:Location:2900774186"),
                "Carryable_TBO_InventoryContainer_2SCU",
                1));

            var loc = store.GetLocation(2900774186)!;
            Assert.Equal(2900774186, loc.Id);
            Assert.Null(loc.Label);
            Assert.Equal(T0, loc.LastSeenUtc);
        }
    }

    [Fact]
    public void Equipped_writes_loadout_entry()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            ApplierFor(store).Apply(new EquippedItemEvent(
                T0, "Arcadiius",
                ItemClass: "rsi_odyssey_undersuit_01_01_01",
                InstanceName: "rsi_odyssey_undersuit_01_01_01_217",
                EntityId: 217,
                Port: "Armor_Undersuit",
                Status: "persistent"));

            Assert.NotNull(store.GetEquipped("Arcadiius", "Armor_Undersuit"));
        }
    }

    [Fact]
    public void ContainerClosed_updates_last_seen_on_existing_location()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var applier = ApplierFor(store);
            applier.Apply(new ContainerOpenedEvent(T0, "Arcadiius",
                new InventoryRef(0, InventoryKind.Location, 1, "0:Location:1"),
                "box", 1));
            var t1 = T0.AddSeconds(30);
            applier.Apply(new ContainerClosedEvent(t1, "Arcadiius",
                new InventoryRef(0, InventoryKind.Location, 1, "0:Location:1")));

            Assert.Equal(t1, store.GetLocation(1)!.LastSeenUtc);
        }
    }

    [Fact]
    public void GridItemCount_is_ignored_without_error()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            ApplierFor(store).Apply(new GridItemCountEvent(T0, 1, 5, 1));
            // no exception, no rows
            Assert.Empty(store.GetHoldingsForLocation(1));
        }
    }

    [Fact]
    public void Audit_records_every_applied_event_with_its_type_and_timestamp()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var applier = ApplierFor(store);
            applier.Apply(new ContainerOpenedEvent(T0, "Arcadiius",
                new InventoryRef(0, InventoryKind.Location, 1, "0:Location:1"),
                "box", 1));
            applier.Apply(new GridItemCountEvent(T0.AddSeconds(1), 1, 1, 1));

            var audit = store.ReadAudit().ToList();
            Assert.Equal(2, audit.Count);
            Assert.Equal("ContainerOpenedEvent", audit[0].Type);
            Assert.Equal("GridItemCountEvent", audit[1].Type);
        }
    }

    // ---------- end-to-end through the real parser ----------

    [Fact]
    public void EndToEnd_synergy_box_session_recovers_bottles_back_in_the_box()
    {
        // Real session: opened box (601), took 2 bottles out into backpack (595), put them back.
        // The discovery-ledger should end up showing the box holds 2 bottles, backpack empty.
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var reader = new InventoryLogReader();
            var applier = ApplierFor(store);

            var path = Path.Combine(
                AppContext.BaseDirectory, "Fixtures", "synergy-box-session.log");
            foreach (var ev in reader.Read(File.ReadLines(path)))
                applier.Apply(ev);

            var item = store.GetItem("Drink_bottle_synergy_01_plus_a")!;
            Assert.Equal(2, store.GetHolding(601563981557, item.Id)!.Quantity);
            Assert.Null(store.GetHolding(595318982158, item.Id));

            Assert.NotNull(store.GetLocation(2900774186));
        }
    }
}
