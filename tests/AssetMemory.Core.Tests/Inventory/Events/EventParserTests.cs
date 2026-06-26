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
}
