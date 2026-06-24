using AssetMemory.Core.Inventory;
using AssetMemory.Core.Inventory.Events;
using AssetMemory.Core.Resolution;

namespace AssetMemory.Core.Tests.Resolution;

public class ResolutionEndToEndTests
{
    private static string Fixture(string name)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void Curated_override_resolves_real_synergy_bottle_move_event()
    {
        var reader = new InventoryLogReader();
        var resolver = new ItemNameResolver(new Dictionary<string, string>
        {
            ["Drink_bottle_synergy_01_plus_a"] = "Synergy+ Bottle",
        });

        var move = reader.Read(File.ReadLines(Fixture("synergy-box-session.log")))
            .OfType<ItemMovedEvent>()
            .First();

        Assert.Equal("Synergy+ Bottle", resolver.Resolve(move.ItemClass));
    }

    [Fact]
    public void Heuristic_fallback_resolves_real_equipped_backpack_class()
    {
        var reader = new InventoryLogReader();
        var resolver = new ItemNameResolver();

        var backpack = reader.Read(File.ReadLines(Fixture("equipped-loadout.log")))
            .OfType<EquippedItemEvent>()
            .Single(e => e.Port == "Armor_Backpack");

        Assert.Equal("Hdtc utility light backpack 01 01 01", resolver.Resolve(backpack.ItemClass));
    }

    [Fact]
    public void Location_labels_show_curated_name_when_known_and_fallback_otherwise()
    {
        var reader = new InventoryLogReader();
        var labels = new LocationLabelResolver(new Dictionary<long, string>
        {
            [2900774186] = "Aaron Halo Outpost",
        });

        var opened = reader.Read(File.ReadLines(Fixture("synergy-box-session.log")))
            .OfType<ContainerOpenedEvent>()
            .Single();

        Assert.Equal("Aaron Halo Outpost", labels.ResolveOrFallback(opened.Container.Id));
        Assert.Equal("Location 99999", labels.ResolveOrFallback(99999));
    }
}
