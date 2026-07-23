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

    // ---------- drops: flagged bucket + freight-descent merge ----------
    // A drop leaves its source and is credited to the flagged "Dropped" bucket (id -1). If a freight
    // elevator is sent down within the merge window, the drop was a station deposit and is moved onto
    // that station's inventory (same Location:placeId a locker move uses); otherwise it stays flagged
    // as Dropped -- a mission-site / ground drop. "Other" is never used.

    private const long DroppedBucket = -1;

    private static ItemDroppedEvent Drop(DateTimeOffset at, string cls, int qty = 1, long entityId = 724751852287)
        => new(at, "Arcadiius", cls, qty,
            new InventoryRef(706759485577, InventoryKind.Container, 0, "706759485577:Container:0"),
            entityId, RequestId: 89);

    [Fact]
    public void Dropped_without_a_descent_lands_in_the_flagged_Dropped_bucket()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var names = new ItemNameResolver(new Dictionary<string, string>
            {
                ["srvl_armor_heavy_helmet_01_01_10"] = "Overlord Helmet Mirador",
            });
            var applier = ApplierFor(store, names);
            store.UpsertLocation(706759485577, T0, null);
            var item = store.EnsureItem("srvl_armor_heavy_helmet_01_01_10", "Overlord Helmet Mirador");
            store.AdjustHolding(706759485577, item, 1, T0);

            applier.Apply(Drop(T0.AddSeconds(1), "srvl_armor_heavy_helmet_01_01_10"));

            Assert.Null(store.GetHolding(706759485577, item));                // debited out of the backpack
            Assert.Equal(1, store.GetHolding(DroppedBucket, item)!.Quantity); // credited to the Dropped bucket
            Assert.Equal("Dropped", store.GetLocation(DroppedBucket)!.Label);
            var systems = store.GetSystemsWithHoldings().ToList();
            Assert.Contains("Dropped", systems);
            Assert.DoesNotContain("Other", systems);
        }
    }

    [Fact]
    public void Freight_descent_moves_recent_drops_onto_the_current_station_inventory()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var names = new ItemNameResolver(new Dictionary<string, string>
            {
                ["srvl_armor_heavy_helmet_01_01_10"] = "Overlord Helmet Mirador",
            });
            var applier = ApplierFor(store, names);

            applier.Apply(new StationIdentifiedEvent(T0, "Arcadiius", PlaceId: 3723364946, StationCode: "Nyx_Levski"));
            applier.Apply(Drop(T0.AddSeconds(1), "srvl_armor_heavy_helmet_01_01_10"));
            applier.Apply(new FreightDescendedEvent(T0.AddSeconds(11), "Nyx"));

            var item = store.GetItem("srvl_armor_heavy_helmet_01_01_10")!;
            Assert.Equal(1, store.GetHolding(3723364946, item.Id)!.Quantity);  // landed on Levski's inventory
            Assert.Null(store.GetHolding(DroppedBucket, item.Id));             // no longer flagged
            var systems = store.GetSystemsWithHoldings().ToList();
            Assert.Contains("Nyx", systems);
            Assert.DoesNotContain("Other", systems);
            Assert.DoesNotContain("Dropped", systems);                        // Dropped bucket emptied
            // Rolls up under Nyx > Levski exactly like a locker move.
            Assert.Equal(1, store.GetHoldingDetailsPage(3723364946, null, null, "item", true, 1, 50).TotalUnits);
        }
    }

    [Fact]
    public void Freight_descent_beyond_the_merge_window_leaves_the_drop_flagged()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var applier = ApplierFor(store);
            applier.Apply(new StationIdentifiedEvent(T0, "Arcadiius", PlaceId: 3723364946, StationCode: "Nyx_Levski"));
            applier.Apply(Drop(T0.AddSeconds(1), "medpen"));
            applier.Apply(new FreightDescendedEvent(T0.AddSeconds(120), "Nyx"));  // > 60s window

            var item = store.GetItem("medpen")!;
            Assert.Equal(1, store.GetHolding(DroppedBucket, item.Id)!.Quantity);  // stays flagged
            Assert.Null(store.GetHolding(3723364946, item.Id));                   // not delivered
        }
    }

    [Fact]
    public void Freight_descent_without_a_known_station_leaves_the_drop_flagged()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var applier = ApplierFor(store);
            applier.Apply(Drop(T0, "medpen"));
            applier.Apply(new FreightDescendedEvent(T0.AddSeconds(5), "Nyx"));  // no place ever identified

            var item = store.GetItem("medpen")!;
            Assert.Equal(1, store.GetHolding(DroppedBucket, item.Id)!.Quantity);
        }
    }

    [Fact]
    public void Freight_inventory_grid_supplies_the_place_a_following_descent_delivers_to()
    {
        // Without opening the station panel this session, the freight grid line still names the place
        // id; the place must already be labelled (from any prior id) for the merge to land.
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var applier = ApplierFor(store);
            store.UpsertLocation(3723364946, T0, "Levski", "Nyx");   // labelled station row exists
            applier.Apply(Drop(T0.AddSeconds(1), "medpen"));
            applier.Apply(new FreightInventoryEvent(T0.AddSeconds(2), PlaceId: 3723364946));
            applier.Apply(new FreightDescendedEvent(T0.AddSeconds(6), "Nyx"));

            var item = store.GetItem("medpen")!;
            Assert.Equal(1, store.GetHolding(3723364946, item.Id)!.Quantity);
            Assert.Null(store.GetHolding(DroppedBucket, item.Id));
        }
    }

    [Fact]
    public void PlayerLocation_reconnects_the_place_so_a_following_descent_delivers_there()
    {
        // A loading-platform hint ("Levski") reconnects to a place a prior station id minted (by label),
        // so even without re-opening the panel this session a freight descent lands the drop at Levski.
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var applier = ApplierFor(store);
            applier.Apply(new StationIdentifiedEvent(T0, "Arcadiius", PlaceId: 3723364946, StationCode: "Nyx_Levski"));
            applier.ResetSessionState();  // fresh session: place row persists, in-memory state does not

            applier.Apply(new PlayerLocationEvent(T0.AddSeconds(1), "Levski"));
            applier.Apply(Drop(T0.AddSeconds(2), "medpen"));
            applier.Apply(new FreightDescendedEvent(T0.AddSeconds(8), "Nyx"));

            var item = store.GetItem("medpen")!;
            Assert.Equal(1, store.GetHolding(3723364946, item.Id)!.Quantity);
            Assert.Null(store.GetHolding(DroppedBucket, item.Id));
        }
    }

    [Fact]
    public void ResetSessionState_clears_pending_freight_so_a_later_descent_cannot_claim_an_old_drop()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var applier = ApplierFor(store);
            applier.Apply(new StationIdentifiedEvent(T0, "Arcadiius", PlaceId: 3723364946, StationCode: "Nyx_Levski"));
            applier.Apply(Drop(T0.AddSeconds(1), "medpen"));

            applier.ResetSessionState();

            applier.Apply(new FreightDescendedEvent(T0.AddSeconds(6), "Nyx"));

            var item = store.GetItem("medpen")!;
            Assert.Equal(1, store.GetHolding(DroppedBucket, item.Id)!.Quantity);  // still flagged
            Assert.Null(store.GetHolding(3723364946, item.Id));                   // not claimed
        }
    }

    [Fact]
    public void Station_identified_ignores_the_invalid_location_sentinel()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            ApplierFor(store).Apply(new StationIdentifiedEvent(
                T0, "Arcadiius", PlaceId: 999, StationCode: "INVALID_LOCATION_ID"));
            Assert.Null(store.GetLocation(999));  // no bogus place row minted
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
    public void EquipFromInventory_debits_the_item_out_of_the_source_box()
    {
        // The reported bug: equipping straight out of an SCU box left the item still shown in the box
        // because nothing debited it. The equip event must remove one unit from the source container.
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var applier = ApplierFor(store);
            store.UpsertLocation(681562156430, T0, "Stor-All 2 SCU");
            var item = store.EnsureItem("vgl_flightsuit_helmet_01_03_01", "Tailwind Flight Helmet Big Bite");
            store.AdjustHolding(681562156430, item, 1, T0);

            applier.Apply(new ItemEquippedFromInventoryEvent(
                T0.AddSeconds(1),
                ItemClass: "vgl_flightsuit_helmet_01_03_01",
                Source: new InventoryRef(681562156430, InventoryKind.Container, 0, "681562156430:Container:0"),
                Port: "Body_ItemPort:Armor_Undersuit:Armor_Helmet",
                RequestId: 52));

            Assert.Null(store.GetHolding(681562156430, item));  // debited to zero -> gone from the box
        }
    }

    [Fact]
    public void EquipFromInventory_decrements_only_one_unit_of_a_stack()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var applier = ApplierFor(store);
            store.UpsertLocation(681562156430, T0, "Stor-All 2 SCU");
            var item = store.EnsureItem("medpen", "Medpen");
            store.AdjustHolding(681562156430, item, 3, T0);

            applier.Apply(new ItemEquippedFromInventoryEvent(
                T0.AddSeconds(1), "medpen",
                new InventoryRef(681562156430, InventoryKind.Container, 0, "681562156430:Container:0"),
                "Body_ItemPort", 1));

            Assert.Equal(2, store.GetHolding(681562156430, item)!.Quantity);
        }
    }

    [Fact]
    public void Store_credits_the_item_into_the_destination_box()
    {
        // Mirror of the equip fix: unequipping/storing an item into a box must add one unit to it.
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var applier = ApplierFor(store);
            store.UpsertLocation(681562156430, T0, "Stor-All 2 SCU");

            applier.Apply(new ItemStoredEvent(
                T0.AddSeconds(1),
                ItemClass: "qrt_utility_heavy_helmet_01_01_03",
                Target: new InventoryRef(681562156430, InventoryKind.Container, 0, "681562156430:Container:0"),
                RequestId: 43));

            var item = store.GetItem("qrt_utility_heavy_helmet_01_01_03")!;
            Assert.Equal(1, store.GetHolding(681562156430, item.Id)!.Quantity);
        }
    }

    [Fact]
    public void Store_ignores_a_client_only_destination_so_no_phantom_holding_is_minted()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            ApplierFor(store).Apply(new ItemStoredEvent(
                T0, "medpen",
                new InventoryRef(204821708183, InventoryKind.ClientOnly, 1, "204821708183:ClientOnly:1"),
                RequestId: 1));

            var item = store.GetItem("medpen");
            Assert.True(item is null || store.GetHolding(204821708183, item.Id) is null);
        }
    }

    [Fact]
    public void Equip_then_store_round_trips_the_box_holding_back_to_where_it_started()
    {
        // The two new events are inverses: a box holding one helmet, equipped out (-> 0, gone) then
        // stored back in (-> 1) ends exactly where it began.
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var applier = ApplierFor(store);
            store.UpsertLocation(681562156430, T0, "Stor-All 2 SCU");
            var item = store.EnsureItem("vgl_flightsuit_helmet_01_03_01", "Tailwind Flight Helmet Big Bite");
            store.AdjustHolding(681562156430, item, 1, T0);
            var box = new InventoryRef(681562156430, InventoryKind.Container, 0, "681562156430:Container:0");

            applier.Apply(new ItemEquippedFromInventoryEvent(
                T0.AddSeconds(1), "vgl_flightsuit_helmet_01_03_01", box, "Armor_Helmet", 52));
            Assert.Null(store.GetHolding(681562156430, item));  // gone after equip

            applier.Apply(new ItemStoredEvent(
                T0.AddSeconds(2), "vgl_flightsuit_helmet_01_03_01", box, 60));
            Assert.Equal(1, store.GetHolding(681562156430, item)!.Quantity);  // back after store
        }
    }

    [Fact]
    public void EquipFromInventory_ignores_a_client_only_source_so_no_phantom_holding_is_minted()
    {
        // Equipping from the personal pool (a ClientOnly ref) has no tracked holdings row; it must not
        // create a bogus negative/phantom row on the player's own GEID.
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            ApplierFor(store).Apply(new ItemEquippedFromInventoryEvent(
                T0, "medpen",
                new InventoryRef(204821708183, InventoryKind.ClientOnly, 1, "204821708183:ClientOnly:1"),
                "Body_ItemPort", 1));

            var item = store.GetItem("medpen");
            Assert.True(item is null || store.GetHolding(204821708183, item.Id) is null);
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
    public void Station_identified_labels_its_place_id_with_the_resolved_name()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            ApplierFor(store).Apply(new StationIdentifiedEvent(
                T0, "Arrogant", PlaceId: 3170699229, StationCode: "Stanton4_NewBabbage"));

            Assert.Equal("New Babbage", store.GetLocation(3170699229)!.Label);
        }
    }

    [Fact]
    public void Station_identified_derives_and_persists_the_systems_bucket()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            ApplierFor(store).Apply(new StationIdentifiedEvent(
                T0, "Arrogant", PlaceId: 3170699229, StationCode: "Stanton4_NewBabbage"));
            var item = store.EnsureItem("medpen", "Medpen");
            store.AdjustHolding(3170699229, item, 1, T0);

            Assert.Equal(3170699229, Assert.Single(store.GetPlacesWithHoldings("Stanton")).Id);
            Assert.Empty(store.GetPlacesWithHoldings("Nyx"));
        }
    }

    [Fact]
    public void Move_to_a_station_is_keyed_on_place_id_not_the_owning_geid()
    {
        // A station ref is GEID:Location:placeId — holdings must accrue under placeId (3170699229),
        // not the GEID (200146296252) which is identical across every station the player visits.
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var applier = ApplierFor(store);
            applier.Apply(new StationIdentifiedEvent(
                T0, "Arrogant", PlaceId: 3170699229, StationCode: "Stanton4_NewBabbage"));
            applier.Apply(new ItemMovedEvent(T0.AddSeconds(1),
                "Arrogant", "medpen", 3,
                new InventoryRef(9551313454351, InventoryKind.Container, 0, "9551313454351:Container:0"),
                new InventoryRef(200146296252, InventoryKind.Location, 3170699229, "200146296252:Location:3170699229"),
                1));

            var item = store.GetItem("medpen")!;
            Assert.Equal(3, store.GetHolding(3170699229, item.Id)!.Quantity);  // keyed on placeId
            Assert.Null(store.GetHolding(200146296252, item.Id));               // not on the GEID
            Assert.Equal("New Babbage", store.GetLocation(3170699229)!.Label);  // label preserved
        }
    }

    [Fact]
    public void Container_identified_labels_the_box_geid_so_its_holdings_surface()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var applier = ApplierFor(store);
            applier.Apply(new ContainerIdentifiedEvent(
                T0, ContainerId: 681562156430,
                ContainerClass: "Carryable_TBO_InventoryContainer_2SCU", ScuSize: 2,
                ParentLocationId: 141810852));
            applier.Apply(new ItemMovedEvent(T0.AddSeconds(1),
                "Arcadiius", "behr_shotgun_ballistic_01", 1,
                new InventoryRef(204821708183, InventoryKind.Location, 141810852, "204821708183:Location:141810852"),
                new InventoryRef(681562156430, InventoryKind.Container, 0, "681562156430:Container:0"),
                1));

            // A container ref keys off its owning GEID; the box carries a label so the UI shows it,
            // and the moved item lands there.
            Assert.Equal("Stor-All 2 SCU", store.GetLocation(681562156430)!.Label);
            var item = store.GetItem("behr_shotgun_ballistic_01")!;
            Assert.Equal(1, store.GetHolding(681562156430, item.Id)!.Quantity);

            // The box nests under its place: reachable via the container dropdown query...
            var boxes = store.GetContainersForPlace(141810852).ToList();
            Assert.Equal(681562156430, Assert.Single(boxes).Id);
            // ...and its units roll up into the place scope and the "all locations" aggregate.
            Assert.Equal(1, store.GetHoldingDetailsPage(141810852, null, null, "item", true, 1, 50).TotalUnits);
            Assert.Equal(1, store.GetHoldingDetailsPage(null, null, null, "item", true, 1, 50).TotalUnits);
        }
    }

    [Fact]
    public void Container_identified_after_a_move_labels_the_already_created_box_row()
    {
        // Moves seed the box row with a null label first; identification then names it in place.
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var applier = ApplierFor(store);
            applier.Apply(new ItemMovedEvent(T0,
                "Arcadiius", "behr_shotgun_ballistic_01", 1,
                new InventoryRef(204821708183, InventoryKind.Location, 141810852, "204821708183:Location:141810852"),
                new InventoryRef(681562156430, InventoryKind.Container, 0, "681562156430:Container:0"),
                1));
            Assert.Null(store.GetLocation(681562156430)!.Label);

            applier.Apply(new ContainerIdentifiedEvent(
                T0.AddSeconds(1), 681562156430, "Carryable_TBO_InventoryContainer_8SCU", 8, 141810852));

            Assert.Equal("Stor-All 8 SCU", store.GetLocation(681562156430)!.Label);
            var item = store.GetItem("behr_shotgun_ballistic_01")!;
            Assert.Equal(1, store.GetHolding(681562156430, item.Id)!.Quantity);  // holding preserved
        }
    }

    [Fact]
    public void A_later_move_does_not_wipe_an_identified_box_label()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var applier = ApplierFor(store);
            applier.Apply(new ContainerIdentifiedEvent(
                T0, 681562156430, "Carryable_TBO_InventoryContainer_2SCU", 2, 141810852));
            // A subsequent move upserts the same location with a null label — COALESCE must keep it.
            applier.Apply(new ItemMovedEvent(T0.AddSeconds(1),
                "Arcadiius", "behr_shotgun_ballistic_01", 1,
                new InventoryRef(681562156430, InventoryKind.Container, 0, "681562156430:Container:0"),
                new InventoryRef(204821708183, InventoryKind.Location, 141810852, "204821708183:Location:141810852"),
                1));

            Assert.Equal("Stor-All 2 SCU", store.GetLocation(681562156430)!.Label);
        }
    }

    [Fact]
    public void Items_moved_from_an_unlabelled_backpack_into_a_box_track_and_roll_up_under_the_place()
    {
        // Evaluation proof: moving OUT of a backpack (an unlabelled Container) INTO an identified
        // box is tracked by the same ledger as any move -- the units land in the box and, because
        // the box nests under its place, they stay visible in the place + all-locations rollup.
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var applier = ApplierFor(store);
            store.UpsertLocation(141810852, T0, "Nyx Castra Jump Point");        // the place
            applier.Apply(new ContainerIdentifiedEvent(
                T0, 681562156430, "Carryable_TBO_InventoryContainer_2SCU", 2, 141810852));  // box @ place
            applier.Apply(new ItemMovedEvent(T0.AddSeconds(1),
                "Arcadiius", "medpen", 2,
                new InventoryRef(595318982158, InventoryKind.Container, 0, "595318982158:Container:0"), // backpack
                new InventoryRef(681562156430, InventoryKind.Container, 0, "681562156430:Container:0"), // box
                1));

            var item = store.GetItem("medpen")!;
            Assert.Equal(2, store.GetHolding(681562156430, item.Id)!.Quantity);   // landed in the box
            Assert.Null(store.GetHolding(595318982158, item.Id));                 // not left in the backpack

            // Visible in the place rollup, the box drill-down, and the all-locations aggregate.
            Assert.Equal(2, store.GetHoldingDetailsPage(141810852, null, null, "item", true, 1, 50).TotalUnits);
            Assert.Equal(2, store.GetHoldingDetailsPage(141810852, 681562156430, null, "item", true, 1, 50).TotalUnits);
            Assert.Equal(2, store.GetHoldingDetailsPage(null, null, null, "item", true, 1, 50).TotalUnits);
        }
    }

    // ---------- worn backpack naming (the "Move all into a backpack" gap) ----------

    [Fact]
    public void Worn_backpack_is_named_from_the_loadout_and_move_all_into_it_nests_under_the_current_place()
    {
        // The reported gap: "Move all" into a worn backpack tracked the items, but under a Container GEID
        // that was never OpenNestedInventory'd -- so it had no label/parent and the items orphaned out of
        // view. Equipping the backpack now names that GEID, and the following move (once the station has
        // been identified) anchors it under the current place, exactly like an SCU box.
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var names = new ItemNameResolver(new Dictionary<string, string>
            {
                ["rrs_combat_heavy_backpack_01_01_10"] = "Morozov-CH Backpack Thule",
                ["thp_light_helmet_01_01_01"] = "Aztalan Helmet",
            });
            var applier = ApplierFor(store, names);
            const long backpack = 735313847862;
            const long place = 308639451;

            // Spawn loadout attaches the backpack BEFORE any station is identified (as in the real log).
            applier.Apply(new EquippedItemEvent(
                T0, "Arcadiius",
                ItemClass: "rrs_combat_heavy_backpack_01_01_10",
                InstanceName: "rrs_combat_heavy_backpack_01_01_10_735313847862",
                EntityId: backpack,
                Port: "backpack",
                Status: "persistent"));
            applier.Apply(new StationIdentifiedEvent(T0.AddSeconds(1), "Arcadiius", place, "RR_HUR_LEO"));
            applier.Apply(new ItemMovedEvent(T0.AddSeconds(2),
                "Arcadiius", "thp_light_helmet_01_01_01", 1,
                new InventoryRef(204821708183, InventoryKind.Location, place, $"204821708183:Location:{place}"),
                new InventoryRef(backpack, InventoryKind.Container, 0, $"{backpack}:Container:0"),
                1));

            // The backpack GEID is labelled from the loadout and nests under the station.
            Assert.Equal("Morozov-CH Backpack Thule", store.GetLocation(backpack)!.Label);
            Assert.Equal(backpack, Assert.Single(store.GetContainersForPlace(place)).Id);

            // The moved item lands in the backpack and rolls up under the place + all-locations aggregate.
            var item = store.GetItem("thp_light_helmet_01_01_01")!;
            Assert.Equal(1, store.GetHolding(backpack, item.Id)!.Quantity);
            Assert.Equal(1, store.GetHoldingDetailsPage(place, null, null, "item", true, 1, 50).TotalUnits);
            Assert.Equal(1, store.GetHoldingDetailsPage(place, backpack, null, "item", true, 1, 50).TotalUnits);
            Assert.Equal(1, store.GetHoldingDetailsPage(null, null, null, "item", true, 1, 50).TotalUnits);
        }
    }

    [Fact]
    public void Worn_backpack_with_no_known_place_is_still_named_as_a_top_level_row()
    {
        // Even if a place is never identified, the backpack is named off the loadout so its contents
        // surface under a labelled row instead of a blank, unidentified one.
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var names = new ItemNameResolver(new Dictionary<string, string>
            {
                ["rrs_combat_heavy_backpack_01_01_10"] = "Morozov-CH Backpack Thule",
            });
            const long backpack = 735313847862;

            ApplierFor(store, names).Apply(new EquippedItemEvent(
                T0, "Arcadiius", "rrs_combat_heavy_backpack_01_01_10",
                "rrs_combat_heavy_backpack_01_01_10_735313847862", backpack, "backpack", "persistent"));

            Assert.Equal("Morozov-CH Backpack Thule", store.GetLocation(backpack)!.Label);
        }
    }

    [Fact]
    public void Equipping_a_non_container_port_does_not_mint_a_location_row()
    {
        // Only the backpack port doubles as a tracked container; helmet / armor / weapon ports must not
        // create a phantom location row keyed on their loadout EntityId.
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            ApplierFor(store).Apply(new EquippedItemEvent(
                T0, "Arcadiius", "rsi_odyssey_undersuit_01_01_01",
                "rsi_odyssey_undersuit_01_01_01_217", EntityId: 217, Port: "Armor_Undersuit", Status: "persistent"));

            Assert.Null(store.GetLocation(217));
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

    // ---------- batched apply ----------

    [Fact]
    public void ApplyBatch_applies_every_event_same_as_looped_individual_apply_calls()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var reader = new InventoryLogReader();
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "synergy-box-session.log");
            var events = reader.Read(File.ReadLines(path));

            var count = ApplierFor(store).ApplyBatch(events);

            Assert.True(count > 0);
            var item = store.GetItem("Drink_bottle_synergy_01_plus_a")!;
            Assert.Equal(2, store.GetHolding(601563981557, item.Id)!.Quantity);
            Assert.Null(store.GetHolding(595318982158, item.Id));
            Assert.NotNull(store.GetLocation(2900774186));
        }
    }

    [Fact]
    public void ApplyBatch_rolls_back_every_event_in_the_batch_if_one_of_them_throws()
    {
        // EnsureItem rejects an empty class name. That failure should undo the whole batch,
        // not just the offending event -- the point of wrapping the batch in one transaction.
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var events = new InventoryEvent[]
            {
                new ContainerOpenedEvent(T0, "Arcadiius",
                    new InventoryRef(0, InventoryKind.Location, 1, "0:Location:1"), "box", 1),
                new ItemMovedEvent(T0.AddSeconds(1), "Arcadiius", "", 1,
                    new InventoryRef(11, InventoryKind.Container, 0, "11:Container:0"),
                    new InventoryRef(22, InventoryKind.Container, 0, "22:Container:0"), 1),
            };

            Assert.ThrowsAny<ArgumentException>(() => ApplierFor(store).ApplyBatch(events));

            Assert.Null(store.GetLocation(1));
            Assert.Empty(store.ReadAudit());
        }
    }

    // ---------- sync-inception lower bound ----------

    private static ItemMovedEvent MoveAt(DateTimeOffset when, int qty)
        => new(when, "Arcadiius", "medpen", qty,
            new InventoryRef(11, InventoryKind.Container, 0, "11:Container:0"),
            new InventoryRef(22, InventoryKind.Container, 0, "22:Container:0"),
            1);

    [Fact]
    public void ApplyBatch_with_inception_drops_events_before_the_date_and_keeps_the_rest()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var applier = ApplierFor(store);
            applier.InceptionUtc = T0;  // keep T0 and later, drop anything earlier

            var count = applier.ApplyBatch(new InventoryEvent[]
            {
                MoveAt(T0.AddSeconds(-1), 5),  // before → dropped
                MoveAt(T0, 3),                 // on the boundary → kept (inclusive)
                MoveAt(T0.AddSeconds(1), 2),   // after → kept
            });

            Assert.Equal(2, count);  // only the two in-window events counted
            var item = store.GetItem("medpen")!;
            Assert.Equal(5, store.GetHolding(22, item.Id)!.Quantity);  // 3 + 2, the dropped 5 excluded
        }
    }

    [Fact]
    public void ApplyBatch_with_inception_does_not_audit_dropped_events()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var applier = ApplierFor(store);
            applier.InceptionUtc = T0;

            applier.ApplyBatch(new InventoryEvent[] { MoveAt(T0.AddSeconds(-1), 5) });

            Assert.Empty(store.ReadAudit());  // a filtered event leaves no trace at all
        }
    }

    [Fact]
    public void ApplyBatch_without_inception_ingests_everything()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var applier = ApplierFor(store);  // InceptionUtc left null

            var count = applier.ApplyBatch(new InventoryEvent[]
            {
                MoveAt(T0.AddSeconds(-100), 4),
                MoveAt(T0, 1),
            });

            Assert.Equal(2, count);
            var item = store.GetItem("medpen")!;
            Assert.Equal(5, store.GetHolding(22, item.Id)!.Quantity);
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
