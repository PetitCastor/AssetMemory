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
