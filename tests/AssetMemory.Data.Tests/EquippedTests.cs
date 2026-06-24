using AssetMemory.Data;

namespace AssetMemory.Data.Tests;

public class EquippedTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 24, 19, 22, 0, TimeSpan.Zero);

    [Fact]
    public void UpsertEquipped_inserts_a_new_loadout_entry()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var item = store.EnsureItem("rsi_odyssey_undersuit_01_01_01", "RSI Odyssey Undersuit");

            store.UpsertEquipped(
                player: "Arcadiius",
                port: "Armor_Undersuit",
                itemId: item,
                entityId: 200000000217,
                instanceName: "rsi_odyssey_undersuit_01_01_01_200000000217",
                status: "persistent",
                atUtc: T0);

            var eq = store.GetEquipped("Arcadiius", "Armor_Undersuit")!;
            Assert.Equal(item, eq.ItemId);
            Assert.Equal(200000000217, eq.EntityId);
            Assert.Equal("persistent", eq.Status);
            Assert.Equal(T0, eq.LastSeenUtc);
        }
    }

    [Fact]
    public void UpsertEquipped_replaces_existing_loadout_entry_on_same_port()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var oldItem = store.EnsureItem("old_helmet", "Old Helmet");
            var newItem = store.EnsureItem("new_helmet", "New Helmet");

            store.UpsertEquipped("Arcadiius", "Armor_Helmet", oldItem, 1, "old_1", "persistent", T0);
            store.UpsertEquipped("Arcadiius", "Armor_Helmet", newItem, 2, "new_2", "persistent", T0.AddMinutes(1));

            var eq = store.GetEquipped("Arcadiius", "Armor_Helmet")!;
            Assert.Equal(newItem, eq.ItemId);
            Assert.Equal(2, eq.EntityId);
        }
    }

    [Fact]
    public void GetEquippedForPlayer_returns_one_row_per_port()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var helmet = store.EnsureItem("helmet", "Helmet");
            var arms = store.EnsureItem("arms", "Arms");
            store.UpsertEquipped("Arcadiius", "Armor_Helmet", helmet, 1, "h", "persistent", T0);
            store.UpsertEquipped("Arcadiius", "Armor_Arms", arms, 2, "a", "persistent", T0);

            var loadout = store.GetEquippedForPlayer("Arcadiius").ToList();
            Assert.Equal(2, loadout.Count);
        }
    }

    [Fact]
    public void GetEquipped_returns_null_when_port_unused()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            Assert.Null(store.GetEquipped("Arcadiius", "Armor_Backpack"));
        }
    }
}
