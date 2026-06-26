using AssetMemory.Core.Inventory;
using AssetMemory.Core.Inventory.Events;
using AssetMemory.Core.Logs;

namespace AssetMemory.Core.Tests.Inventory;

public class InventoryLogReaderTests
{
    private static string Fixture(string name)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    // ---------- medium: aggregate parser dispatch ----------

    [Fact]
    public void Aggregate_returns_false_for_unrelated_lines()
    {
        var parser = new InventoryEventParser();
        Assert.True(LogEntryParser.TryParse(
            "<2026-06-24T19:23:50.563Z> [Notice] <Close Inventory Grid> Player[Arcadiius] closing inventory in progress... [Team_CoreGameplayFeatures][Inventory]",
            out var entry));
        Assert.False(parser.TryParse(entry!, out var ev));
        Assert.Null(ev);
    }

    [Fact]
    public void Reader_skips_lines_that_are_not_log_entries()
    {
        var reader = new InventoryLogReader();
        var events = reader.Read(["", "garbage", "   "]).ToList();
        Assert.Empty(events);
    }

    [Fact]
    public void Reader_identifies_a_station_open_and_ignores_container_opens()
    {
        // Realistic ordering: container opens fire first (no readable name), then a station
        // open arrives as a RequestLocationInventory immediately followed by its numeric ref.
        string[] lines =
        [
            "<2026-03-05T17:23:38.216Z> [Notice] <RequestInventory> Request[0] Inventory[9550546582049:Container:0] [Team_CoreGameplayFeatures][Inventory]",
            "<2026-03-05T17:23:50.781Z> [Notice] <RequestInventory> Request[1] Inventory[9487979850469:Container:0] [Team_CoreGameplayFeatures][Inventory]",
            "<2026-03-05T17:23:51.074Z> [Notice] <RequestLocationInventory> Player[Arrogant] requested inventory for Location[RR_HUR_LEO] [Team_CoreGameplayFeatures][Inventory]",
            "<2026-03-05T17:23:51.074Z> [Notice] <RequestInventory> Request[3] Inventory[200146296252:Location:308639451] [Team_CoreGameplayFeatures][Inventory]",
        ];

        var station = new InventoryLogReader().Read(lines)
            .OfType<StationIdentifiedEvent>()
            .Single();

        Assert.Equal("Arrogant", station.Player);
        Assert.Equal(308639451, station.PlaceId);
        Assert.Equal("RR_HUR_LEO", station.StationCode);
    }

    // ---------- large: end-to-end over a real captured session ----------

    [Fact]
    public void EndToEnd_synergy_box_session_yields_expected_event_sequence()
    {
        var reader = new InventoryLogReader();

        var events = reader.Read(File.ReadLines(Fixture("synergy-box-session.log"))).ToList();

        // open, grid-count, move-out, move-back, close — noise lines filtered out.
        Assert.Collection(events,
            e => Assert.IsType<ContainerOpenedEvent>(e),
            e => Assert.IsType<GridItemCountEvent>(e),
            e => Assert.IsType<ItemMovedEvent>(e),
            e => Assert.IsType<ItemMovedEvent>(e),
            e => Assert.IsType<ContainerClosedEvent>(e));
    }

    [Fact]
    public void EndToEnd_synergy_box_session_recovers_item_identity_and_quantity()
    {
        var reader = new InventoryLogReader();

        var moves = reader.Read(File.ReadLines(Fixture("synergy-box-session.log")))
            .OfType<ItemMovedEvent>()
            .ToList();

        Assert.Equal(2, moves.Count);
        Assert.All(moves, m => Assert.Equal("Drink_bottle_synergy_01_plus_a", m.ItemClass));
        Assert.All(moves, m => Assert.Equal(2, m.Quantity));

        // First move takes the bottles OUT of the box (container 601563981557) into the backpack;
        // second move puts them back.
        Assert.Equal(601563981557, moves[0].Source.Owner);
        Assert.Equal(595318982158, moves[0].Target.Owner);
        Assert.Equal(595318982158, moves[1].Source.Owner);
        Assert.Equal(601563981557, moves[1].Target.Owner);
    }

    [Fact]
    public void EndToEnd_synergy_box_session_captures_the_box_location_on_open()
    {
        var reader = new InventoryLogReader();

        var opened = reader.Read(File.ReadLines(Fixture("synergy-box-session.log")))
            .OfType<ContainerOpenedEvent>()
            .Single();

        Assert.Equal("Carryable_TBO_InventoryContainer_2SCU", opened.ContainerClass);
        Assert.Equal(InventoryKind.Location, opened.Container.Kind);
        Assert.Equal(2900774186, opened.Container.Id);
    }

    [Fact]
    public void EndToEnd_equipped_loadout_recovers_all_ports()
    {
        var reader = new InventoryLogReader();

        var equipped = reader.Read(File.ReadLines(Fixture("equipped-loadout.log")))
            .OfType<EquippedItemEvent>()
            .ToList();

        Assert.Equal(4, equipped.Count);
        Assert.Equal(
            ["Body_ItemPort", "Armor_Undersuit", "Armor_Helmet", "Armor_Backpack"],
            equipped.Select(e => e.Port));
        Assert.Contains(equipped, e => e.ItemClass == "hdtc_utility_light_backpack_01_01_01");
    }
}
