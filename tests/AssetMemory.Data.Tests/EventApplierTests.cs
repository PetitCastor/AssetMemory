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
    public void Dropped_debits_source_and_credits_a_labelled_entity_location()
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

            applier.Apply(new ItemDroppedEvent(
                T0.AddSeconds(1),
                Player: "Arcadiius",
                ItemClass: "srvl_armor_heavy_helmet_01_01_10",
                Quantity: 1,
                Source: new InventoryRef(706759485577, InventoryKind.Container, 0, "706759485577:Container:0"),
                EntityId: 724751852287,
                RequestId: 89));

            Assert.Null(store.GetHolding(706759485577, item));  // debited to zero -> removed
            var dropped = store.GetHolding(724751852287, item)!;
            Assert.Equal(1, dropped.Quantity);
            Assert.Equal("Dropped: Overlord Helmet Mirador", store.GetLocation(724751852287)!.Label);
        }
    }

    [Fact]
    public void Dropped_nests_under_the_last_known_place_instead_of_going_to_other()
    {
        // A freight-elevator (or any ground) drop carries no place of its own -- it should land
        // under wherever the player was last identified, same as a Stor-All box nests under where
        // it was opened, rather than surfacing as a disconnected top-level "Other" row.
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var names = new ItemNameResolver(new Dictionary<string, string>
            {
                ["srvl_armor_heavy_helmet_01_01_10"] = "Overlord Helmet Mirador",
            });
            var applier = ApplierFor(store, names);

            applier.Apply(new StationIdentifiedEvent(
                T0, "Arcadiius", PlaceId: 141810852, StationCode: "RR_JP_NyxCastra"));
            applier.Apply(new ItemDroppedEvent(
                T0.AddSeconds(1), "Arcadiius", "srvl_armor_heavy_helmet_01_01_10", 1,
                new InventoryRef(706759485577, InventoryKind.Container, 0, "706759485577:Container:0"),
                EntityId: 724751852287, RequestId: 89));

            var dropLocation = Assert.Single(store.GetContainersForPlace(141810852));
            Assert.Equal(724751852287, dropLocation.Id);
            Assert.Equal("Dropped: Overlord Helmet Mirador", dropLocation.Label);
            Assert.Equal(1, store.GetHoldingDetailsPage(141810852, null, null, "item", true, 1, 50).TotalUnits);
        }
    }

    [Fact]
    public void ResetSessionState_clears_the_last_known_place_so_a_later_drop_falls_back_to_top_level()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var applier = ApplierFor(store);
            applier.Apply(new StationIdentifiedEvent(
                T0, "Arcadiius", PlaceId: 141810852, StationCode: "RR_JP_NyxCastra"));

            applier.ResetSessionState();

            applier.Apply(new ItemDroppedEvent(
                T0.AddSeconds(1), "Arcadiius", "medpen", 1,
                new InventoryRef(706759485577, InventoryKind.Container, 0, "706759485577:Container:0"),
                EntityId: 724751852287, RequestId: 89));

            Assert.Empty(store.GetContainersForPlace(141810852));  // not nested under the stale place
            var item = store.GetItem("medpen")!;
            Assert.Equal(1, store.GetHolding(724751852287, item.Id)!.Quantity);  // still tracked, just top-level
        }
    }

    // ---------- PlayerLocation (loading-platform hint) ----------

    [Fact]
    public void PlayerLocation_reconnects_a_drop_to_a_place_a_prior_station_id_already_minted()
    {
        // The freight-elevator fix: the player opened Levski's Local Inventory in a past session (so
        // the place row exists) but not this one. A platform hint carrying "Levski" must reconnect to
        // that place by label so the drop nests under Levski instead of a top-level "Other" row --
        // even though the hint itself carries no numeric place id.
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var applier = ApplierFor(store);
            applier.Apply(new StationIdentifiedEvent(
                T0, "Arcadiius", PlaceId: 141810852, StationCode: "Nyx_Levski"));
            applier.ResetSessionState();  // simulate a fresh session: place row persists, in-memory state does not

            applier.Apply(new PlayerLocationEvent(T0.AddSeconds(1), "Levski"));
            applier.Apply(new ItemDroppedEvent(
                T0.AddSeconds(2), "Arcadiius", "medpen", 1,
                new InventoryRef(706759485577, InventoryKind.Container, 0, "706759485577:Container:0"),
                EntityId: 724751852287, RequestId: 89));

            var drop = Assert.Single(store.GetContainersForPlace(141810852));
            Assert.Equal(724751852287, drop.Id);
            Assert.DoesNotContain("Other", store.GetSystemsWithHoldings());
        }
    }

    [Fact]
    public void PlayerLocation_tags_a_placeless_drop_with_the_current_system_not_other()
    {
        // A bare-system hint (a hangar ship/freight elevator names only "Nyx") can't reconnect to a
        // place, but it still pulls the drop out of the "Other" bucket and onto Nyx.
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var applier = ApplierFor(store);
            applier.Apply(new PlayerLocationEvent(T0, "Nyx"));
            applier.Apply(new ItemDroppedEvent(
                T0.AddSeconds(1), "Arcadiius", "medpen", 1,
                new InventoryRef(706759485577, InventoryKind.Container, 0, "706759485577:Container:0"),
                EntityId: 724751852287, RequestId: 89));

            var systems = store.GetSystemsWithHoldings().ToList();
            Assert.Contains("Nyx", systems);
            Assert.DoesNotContain("Other", systems);
            Assert.Equal("Dropped: Medpen", store.GetLocation(724751852287)!.Label); // top-level, labelled row
        }
    }

    [Fact]
    public void ResetSessionState_clears_the_last_known_system_so_a_later_drop_falls_back_to_other()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var applier = ApplierFor(store);
            applier.Apply(new PlayerLocationEvent(T0, "Nyx"));

            applier.ResetSessionState();

            applier.Apply(new ItemDroppedEvent(
                T0.AddSeconds(1), "Arcadiius", "medpen", 1,
                new InventoryRef(706759485577, InventoryKind.Container, 0, "706759485577:Container:0"),
                EntityId: 724751852287, RequestId: 89));

            var systems = store.GetSystemsWithHoldings().ToList();
            Assert.Contains("Other", systems);   // system hint was wiped, so the drop falls back
            Assert.DoesNotContain("Nyx", systems);
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
