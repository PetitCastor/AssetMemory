using AssetMemory.Core.Inventory;
using AssetMemory.Core.Inventory.Events;
using AssetMemory.Core.Logs;

namespace AssetMemory.Core.Tests.Inventory.Events;

public class EventParserTests
{
    private static LogEntry Entry(string line)
    {
        Assert.True(LogEntryParser.TryParse(line, out var entry), $"line did not parse: {line}");
        return entry!;
    }

    // ---------- ContainerOpenedParser ----------

    private const string OpenLine =
        "<2026-06-24T19:22:39.830Z> [Notice] <InventoryManagementRequest> Queued Request[22] Type[OpenNestedInventory] for 'Arcadiius' [204821708183] Source Inventory[204821708183:Location:2900774186] Target Inventory[INVALID]. Source[Carryable_TBO_InventoryContainer_2SCU] amount[0] rank[amqgytfwbdhfn]. Target[NULL] amount[0] rank[]. Item[NONE] action[None]. RequestInProgress[0] CurrentProcess[] [Team_CoreGameplayFeatures][Inventory]";

    [Fact]
    public void Open_parses_to_container_opened_event()
    {
        Assert.True(new ContainerOpenedParser().TryParse(Entry(OpenLine), out var ev));
        var opened = Assert.IsType<ContainerOpenedEvent>(ev);
        Assert.Equal("Arcadiius", opened.Player);
        Assert.Equal(InventoryKind.Location, opened.Container.Kind);
        Assert.Equal(2900774186, opened.Container.Id);
        Assert.Equal("Carryable_TBO_InventoryContainer_2SCU", opened.ContainerClass);
        Assert.Equal(22, opened.RequestId);
    }

    [Fact]
    public void Open_ignores_the_uncommitted_new_request_line()
    {
        const string newReq =
            "<2026-06-24T19:22:39.827Z> [Notice] <InventoryManagement> New request[22] Player[Arcadiius] Type[OpenNestedInventory] SourceInventory[204821708183:Location:2900774186] TargetInventory[INVALID] ItemClass[] [Team_CoreGameplayFeatures][Inventory]";
        Assert.False(new ContainerOpenedParser().TryParse(Entry(newReq), out _));
    }

    [Fact]
    public void Open_ignores_a_move_queued_line()
    {
        Assert.False(new ContainerOpenedParser().TryParse(Entry(MoveLine), out _));
    }

    // ---------- MoveEventParser ----------

    private const string MoveLine =
        "<2026-06-24T19:23:30.687Z> [Notice] <InventoryManagementRequest> Queued Request[26] Type[Move] for 'Arcadiius' [204821708183] Source Inventory[601563981557:Container:0] Target Inventory[595318982158:Container:0]. Source[Drink_bottle_synergy_01_plus_a] amount[2] rank[amqgytlzrhqzn]. Target[NULL] amount[0] rank[amqgqqzvfhycz]. Item[NONE] action[None]. RequestInProgress[0] CurrentProcess[] [Team_CoreGameplayFeatures][Inventory]";

    [Fact]
    public void Move_parses_item_class_and_quantity()
    {
        Assert.True(new MoveEventParser().TryParse(Entry(MoveLine), out var ev));
        var moved = Assert.IsType<ItemMovedEvent>(ev);
        Assert.Equal("Drink_bottle_synergy_01_plus_a", moved.ItemClass);
        Assert.Equal(2, moved.Quantity);
    }

    [Fact]
    public void Move_parses_source_and_target_inventories()
    {
        Assert.True(new MoveEventParser().TryParse(Entry(MoveLine), out var ev));
        var moved = (ItemMovedEvent)ev!;
        Assert.Equal(601563981557, moved.Source.Owner);
        Assert.Equal(InventoryKind.Container, moved.Source.Kind);
        Assert.Equal(595318982158, moved.Target.Owner);
        Assert.Equal("Arcadiius", moved.Player);
        Assert.Equal(26, moved.RequestId);
    }

    [Fact]
    public void Move_ignores_an_open_line()
    {
        Assert.False(new MoveEventParser().TryParse(Entry(OpenLine), out _));
    }

    // ---------- GridItemCountParser ----------

    [Fact]
    public void Grid_parses_stack_and_inventory_counts()
    {
        const string line =
            "<2026-06-24T19:22:39.830Z> [Notice] <GetGridItem> Request[16] Number of Items[1] in Inventories[1] [Team_CoreGameplayFeatures][Inventory]";
        Assert.True(new GridItemCountParser().TryParse(Entry(line), out var ev));
        var grid = Assert.IsType<GridItemCountEvent>(ev);
        Assert.Equal(16, grid.RequestId);
        Assert.Equal(1, grid.StackCount);
        Assert.Equal(1, grid.InventoryCount);
    }

    [Fact]
    public void Grid_ignores_the_succeeded_line()
    {
        const string line =
            "<2026-06-24T19:22:39.875Z> [Notice] <GetGridItem> Request[16] request succeeded. [Team_CoreGameplayFeatures][Inventory]";
        Assert.False(new GridItemCountParser().TryParse(Entry(line), out _));
    }

    // ---------- EquippedItemParser ----------

    [Fact]
    public void Equipped_parses_attachment_triplet_and_port()
    {
        const string line =
            "<2026-06-24T18:46:21.085Z> [Notice] <AttachmentReceived> Player[Arcadiius] Attachment[rsi_odyssey_undersuit_01_01_01_200000000217, rsi_odyssey_undersuit_01_01_01, 200000000217] Status[persistent] Port[Armor_Undersuit] Elapsed[26.766226] [Team_CoreGameplayFeatures][Inventory]";
        Assert.True(new EquippedItemParser().TryParse(Entry(line), out var ev));
        var eq = Assert.IsType<EquippedItemEvent>(ev);
        Assert.Equal("Arcadiius", eq.Player);
        Assert.Equal("rsi_odyssey_undersuit_01_01_01", eq.ItemClass);
        Assert.Equal("rsi_odyssey_undersuit_01_01_01_200000000217", eq.InstanceName);
        Assert.Equal(200000000217, eq.EntityId);
        Assert.Equal("Armor_Undersuit", eq.Port);
        Assert.Equal("persistent", eq.Status);
    }

    // ---------- ContainerClosedParser ----------

    [Fact]
    public void Closed_parses_container_ref()
    {
        const string line =
            "<2026-06-24T19:23:50.563Z> [Notice] <Remove Inventory Container UI> Player[Arcadiius] inventory [204821708183:Location:2900774186] removed from UI [Team_CoreGameplayFeatures][Inventory]";
        Assert.True(new ContainerClosedParser().TryParse(Entry(line), out var ev));
        var closed = Assert.IsType<ContainerClosedEvent>(ev);
        Assert.Equal("Arcadiius", closed.Player);
        Assert.Equal(2900774186, closed.Container.Id);
        Assert.Equal(InventoryKind.Location, closed.Container.Kind);
    }

    [Fact]
    public void Closed_ignores_client_only_is_still_a_valid_ref()
    {
        const string line =
            "<2026-06-24T19:23:50.563Z> [Notice] <Remove Inventory Container UI> Player[Arcadiius] inventory [0:ClientOnly:1] removed from UI [Team_CoreGameplayFeatures][Inventory]";
        Assert.True(new ContainerClosedParser().TryParse(Entry(line), out var ev));
        Assert.Equal(InventoryKind.ClientOnly, ((ContainerClosedEvent)ev!).Container.Kind);
    }

    // ---------- StationInventoryParser ----------
    // A station is a <RequestInventory> with a :Location: ref immediately preceded by a
    // <RequestLocationInventory> that carries the readable code. The parser is stateful:
    // it stashes the code on the first line and emits the paired event on the second.

    private const string NbLocLine =
        "<2026-03-05T21:38:19.398Z> [Notice] <RequestLocationInventory> Player[Arrogant] requested inventory for Location[Stanton4_NewBabbage] [Team_CoreGameplayFeatures][Inventory]";
    private const string NbInvLine =
        "<2026-03-05T21:38:19.399Z> [Notice] <RequestInventory> Request[45] Inventory[200146296252:Location:3170699229] [Team_CoreGameplayFeatures][Inventory]";
    private const string ContainerInvLine =
        "<2026-03-05T17:23:38.216Z> [Notice] <RequestInventory> Request[0] Inventory[9550546582049:Container:0] [Team_CoreGameplayFeatures][Inventory]";

    [Fact]
    public void Station_pairs_location_request_with_following_numeric_ref()
    {
        var parser = new StationInventoryParser();

        // The location-request line stashes state but is not itself an event.
        Assert.False(parser.TryParse(Entry(NbLocLine), out _));
        // The very next request-inventory line completes the pair (note the 1ms timestamp skew).
        Assert.True(parser.TryParse(Entry(NbInvLine), out var ev));

        var s = Assert.IsType<StationIdentifiedEvent>(ev);
        Assert.Equal("Arrogant", s.Player);
        Assert.Equal(3170699229, s.PlaceId);
        Assert.Equal("Stanton4_NewBabbage", s.StationCode);
    }

    // Current game builds tag the numeric-ref line "<Query Inventory>" instead of the older
    // "<RequestInventory>"; identification must still pair off it. (Regression: the whole
    // station-only inventory view was empty because this token was not recognised.)
    private const string NyxLocLine =
        "<2026-07-19T00:05:31.512Z> [Notice] <RequestLocationInventory> Player[Arcadiius] requested inventory for Location[RR_JP_NyxCastra] [Team_CoreGameplayFeatures][Inventory]";
    private const string NyxQueryInvLine =
        "<2026-07-19T00:05:31.513Z> [Notice] <Query Inventory> Request[31] Inventory[204821708183:Location:141810852] [Team_CoreGameplayFeatures][Inventory]";

    [Fact]
    public void Station_pairs_with_current_query_inventory_token()
    {
        var parser = new StationInventoryParser();

        Assert.False(parser.TryParse(Entry(NyxLocLine), out _));
        Assert.True(parser.TryParse(Entry(NyxQueryInvLine), out var ev));

        var s = Assert.IsType<StationIdentifiedEvent>(ev);
        Assert.Equal("Arcadiius", s.Player);
        Assert.Equal(141810852, s.PlaceId);
        Assert.Equal("RR_JP_NyxCastra", s.StationCode);
    }

    [Fact]
    public void Station_ignores_a_bare_container_request_inventory()
    {
        // No preceding location-request → the gate rejects the (extremely common) container open.
        Assert.False(new StationInventoryParser().TryParse(Entry(ContainerInvLine), out _));
    }

    [Fact]
    public void Station_pairing_is_consumed_so_a_later_container_open_is_not_mislabelled()
    {
        var parser = new StationInventoryParser();
        Assert.False(parser.TryParse(Entry(NbLocLine), out _));
        Assert.True(parser.TryParse(Entry(NbInvLine), out _));      // pair consumed here
        Assert.False(parser.TryParse(Entry(ContainerInvLine), out _)); // pending must be cleared
    }

    [Fact]
    public void Station_ignores_a_request_inventory_far_in_time_from_a_dangling_location_request()
    {
        // A location-request with no adjacent partner must not pair with an unrelated later open.
        var parser = new StationInventoryParser();
        Assert.False(parser.TryParse(Entry(NbLocLine), out _));
        const string laterStationInv =
            "<2026-03-05T22:10:00.000Z> [Notice] <RequestInventory> Request[7] Inventory[200146296252:Location:308639451] [Team_CoreGameplayFeatures][Inventory]";
        Assert.False(parser.TryParse(Entry(laterStationInv), out _));
    }

    // ---------- NestedContainerParser (Stor-All SCU boxes) ----------
    // Opening a placed box logs its class on the OpenNestedInventory line but its persistent GEID
    // only on the "<Query Inventory>" line that follows (a few grid-query lines later). The parser
    // pairs the two to name the container its holdings key off.

    private static string OpenBox(string cls, string ts = "2026-07-19T00:06:56.132Z") =>
        $"<{ts}> [Notice] <InventoryManagementRequest> Queued Request[49] Type[OpenNestedInventory] for 'Arcadiius' [204821708183] Source Inventory[204821708183:Location:141810852] Target Inventory[INVALID]. Source[{cls}] amount[0] rank[amqpxvhmrjxjn]. Target[NULL] amount[0] rank[]. Item[NONE] action[None]. [Team_CoreGameplayFeatures][Inventory]";

    // The noisy "Query Inventory" line that carries no Inventory[...] ref — must be skipped without
    // discarding the pending open.
    private const string QueryNoise =
        "<2026-07-19T00:06:56.200Z> [Notice] <Query Inventory> Elapsed[0.061596] for IInventoryAPI::AsyncQueryInventory. [Team_CoreGameplayFeatures][Inventory]";
    private const string BoxQueryLine =
        "<2026-07-19T00:06:56.250Z> [Notice] <Query Inventory> Request[34] Inventory[681562156430:Container:0] [Team_CoreGameplayFeatures][Inventory]";

    [Fact]
    public void Nested_container_pairs_open_class_with_the_following_container_ref()
    {
        var parser = new NestedContainerParser();

        Assert.False(parser.TryParse(Entry(OpenBox("Carryable_TBO_InventoryContainer_2SCU")), out _));
        Assert.False(parser.TryParse(Entry(QueryNoise), out _));   // no ref -> skipped, pending kept
        Assert.True(parser.TryParse(Entry(BoxQueryLine), out var ev));

        var c = Assert.IsType<ContainerIdentifiedEvent>(ev);
        Assert.Equal(681562156430, c.ContainerId);  // the owning GEID, not the :0 id
        Assert.Equal(2, c.ScuSize);
        Assert.Equal("Carryable_TBO_InventoryContainer_2SCU", c.ContainerClass);
        Assert.Equal(141810852, c.ParentLocationId);  // the place (Source Inventory location) the box sits at
    }

    [Fact]
    public void Nested_container_reports_no_parent_when_the_open_has_no_location_source()
    {
        // A nested open whose Source Inventory is a container ref (not a place) yields no parent —
        // the box then surfaces on its own rather than nesting under a bogus place.
        var parser = new NestedContainerParser();
        const string openFromContainer =
            "<2026-07-19T00:06:56.132Z> [Notice] <InventoryManagementRequest> Queued Request[49] Type[OpenNestedInventory] for 'Arcadiius' [204821708183] Source Inventory[999:Container:0] Target Inventory[INVALID]. Source[Carryable_TBO_InventoryContainer_2SCU] amount[0] rank[amqpxvhmrjxjn]. Target[NULL] amount[0] rank[]. Item[NONE] action[None]. [Team_CoreGameplayFeatures][Inventory]";
        Assert.False(parser.TryParse(Entry(openFromContainer), out _));
        Assert.True(parser.TryParse(Entry(BoxQueryLine), out var ev));
        Assert.Equal(0, Assert.IsType<ContainerIdentifiedEvent>(ev).ParentLocationId);
    }

    [Theory]
    [InlineData("Carryable_TBO_InventoryContainer_2SCU", 2)]
    [InlineData("Carryable_TBO_InventoryContainer_4SCU", 4)]
    [InlineData("Carryable_TBO_InventoryContainer_8SCU", 8)]
    public void Nested_container_derives_scu_size_from_the_class_name(string cls, int size)
    {
        var parser = new NestedContainerParser();
        Assert.False(parser.TryParse(Entry(OpenBox(cls)), out _));
        Assert.True(parser.TryParse(Entry(BoxQueryLine), out var ev));
        Assert.Equal(size, Assert.IsType<ContainerIdentifiedEvent>(ev).ScuSize);
    }

    [Fact]
    public void Nested_container_ignores_a_container_query_with_no_pending_open()
    {
        Assert.False(new NestedContainerParser().TryParse(Entry(BoxQueryLine), out _));
    }

    [Fact]
    public void Nested_container_ignores_an_open_whose_class_has_no_scu_size()
    {
        // A nested open for something that is not a Stor-All box must not stash — so the next
        // container query is not mislabelled as a box.
        var parser = new NestedContainerParser();
        Assert.False(parser.TryParse(Entry(OpenBox("Carryable_generic_lootcrate_01")), out _));
        Assert.False(parser.TryParse(Entry(BoxQueryLine), out _));
    }

    [Fact]
    public void Nested_container_ignores_a_container_query_far_in_time_from_the_open()
    {
        var parser = new NestedContainerParser();
        Assert.False(parser.TryParse(Entry(OpenBox("Carryable_TBO_InventoryContainer_2SCU")), out _));
        const string lateQuery =
            "<2026-07-19T00:07:30.000Z> [Notice] <Query Inventory> Request[34] Inventory[681562156430:Container:0] [Team_CoreGameplayFeatures][Inventory]";
        Assert.False(parser.TryParse(Entry(lateQuery), out _));
    }

    [Fact]
    public void Nested_container_ignores_a_station_location_query()
    {
        // A Location-kind query belongs to the station parser, not the box parser.
        var parser = new NestedContainerParser();
        Assert.False(parser.TryParse(Entry(OpenBox("Carryable_TBO_InventoryContainer_2SCU")), out _));
        const string stationQuery =
            "<2026-07-19T00:06:56.260Z> [Notice] <Query Inventory> Request[35] Inventory[204821708183:Location:141810852] [Team_CoreGameplayFeatures][Inventory]";
        Assert.False(parser.TryParse(Entry(stationQuery), out _));
    }

    [Fact]
    public void Nested_container_is_dispatched_end_to_end_by_the_aggregate_parser()
    {
        // Through the full parser: the open line is claimed by ContainerOpenedParser (an open event),
        // and the paired query yields the identification — proving parser ordering is correct.
        var reader = new InventoryLogReader();
        var events = reader.Read([
            OpenBox("Carryable_TBO_InventoryContainer_4SCU"),
            QueryNoise,
            BoxQueryLine,
        ]).ToList();

        Assert.Contains(events, e => e is ContainerOpenedEvent);
        var c = Assert.IsType<ContainerIdentifiedEvent>(Assert.Single(events, e => e is ContainerIdentifiedEvent));
        Assert.Equal(681562156430, c.ContainerId);
        Assert.Equal(4, c.ScuSize);
        Assert.Equal(141810852, c.ParentLocationId);
    }
}
