using AssetMemory.Collector;
using AssetMemory.Core.Resolution;
using AssetMemory.Data;
using AssetMemory.Data.Events;
using Microsoft.Data.Sqlite;

namespace AssetMemory.Collector.Tests;

public class GameLogCollectorTests
{
    private static (AssetMemoryStore store, SqliteConnection conn) NewStore()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var store = new AssetMemoryStore(conn);
        store.ApplyMigration();
        return (store, conn);
    }

    [Fact]
    public void Tick_drains_new_lines_into_the_store_and_reports_event_count()
    {
        using var log = new TempLog();
        var (store, conn) = NewStore();
        using (conn)
        {
            var collector = new GameLogCollector(
                new LogTailer(log.Path),
                new EventApplier(store, new ItemNameResolver()));

            // Two inventory lines plus an unrelated noise line.
            log.Append(
                "<2026-06-24T19:23:30.687Z> [Notice] <InventoryManagementRequest> Queued Request[26] Type[Move] for 'Arcadiius' [204821708183] Source Inventory[601563981557:Container:0] Target Inventory[595318982158:Container:0]. Source[Drink_bottle_synergy_01_plus_a] amount[2] rank[a]. Target[NULL] amount[0] rank[b]. Item[NONE] action[None]. [Team_CoreGameplayFeatures][Inventory]",
                "<2026-06-24T19:23:32.857Z> [Notice] <InventoryManagementRequest> Queued Request[27] Type[Move] for 'Arcadiius' [204821708183] Source Inventory[595318982158:Container:0] Target Inventory[601563981557:Container:0]. Source[Drink_bottle_synergy_01_plus_a] amount[2] rank[c]. Target[NULL] amount[0] rank[d]. Item[NONE] action[None]. [Team_CoreGameplayFeatures][Inventory]",
                "noise line that is not a log entry");

            var ticked = collector.Tick();

            Assert.Equal(2, ticked);
            var item = store.GetItem("Drink_bottle_synergy_01_plus_a")!;
            Assert.Equal(2, store.GetHolding(601563981557, item.Id)!.Quantity);
            Assert.Null(store.GetHolding(595318982158, item.Id));
        }
    }

    [Fact]
    public void Tick_is_a_no_op_when_no_new_lines_have_been_appended()
    {
        using var log = new TempLog();
        var (store, conn) = NewStore();
        using (conn)
        {
            var collector = new GameLogCollector(
                new LogTailer(log.Path),
                new EventApplier(store, new ItemNameResolver()));

            log.Append(
                "<2026-06-24T18:46:21.085Z> [Notice] <AttachmentReceived> Player[Arcadiius] Attachment[helmet_x_1, helmet_x, 1] Status[persistent] Port[Armor_Helmet] Elapsed[0] [Team_CoreGameplayFeatures][Inventory]");

            Assert.Equal(1, collector.Tick());
            Assert.Equal(0, collector.Tick());
            Assert.Equal(0, collector.Tick());
        }
    }

    [Fact]
    public void StartFresh_clears_data_and_only_picks_up_new_lines()
    {
        using var log = new TempLog();
        var (store, conn) = NewStore();
        using (conn)
        {
            var tailer = new LogTailer(log.Path);
            var collector = new GameLogCollector(
                tailer,
                new EventApplier(store, new ItemNameResolver()));

            log.Append(
                "<2026-06-24T19:23:30.687Z> [Notice] <InventoryManagementRequest> Queued Request[26] Type[Move] for 'Arcadiius' [204821708183] Source Inventory[601563981557:Container:0] Target Inventory[595318982158:Container:0]. Source[Drink_bottle_synergy_01_plus_a] amount[2] rank[a]. Target[NULL] amount[0] rank[b]. Item[NONE] action[None]. [Team_CoreGameplayFeatures][Inventory]");
            collector.Tick();
            Assert.NotNull(store.GetItem("Drink_bottle_synergy_01_plus_a"));

            collector.StartFresh(() => store.ClearAll());

            // Old data is gone
            Assert.Null(store.GetItem("Drink_bottle_synergy_01_plus_a"));

            // A tick right after start-fresh reads nothing (tailer seeked to EOF)
            Assert.Equal(0, collector.Tick());

            // New lines written AFTER start-fresh are picked up
            log.Append(
                "<2026-06-24T19:25:00.000Z> [Notice] <InventoryManagementRequest> Queued Request[30] Type[Move] for 'Arcadiius' [204821708183] Source Inventory[11:Container:0] Target Inventory[22:Container:0]. Source[medpen_tier1] amount[1] rank[a]. Target[NULL] amount[0] rank[b]. Item[NONE] action[None]. [Team_CoreGameplayFeatures][Inventory]");
            Assert.Equal(1, collector.Tick());
            Assert.NotNull(store.GetItem("medpen_tier1"));
            Assert.Null(store.GetItem("Drink_bottle_synergy_01_plus_a"));
        }
    }

    [Fact]
    public void EndToEnd_synergy_box_fixture_through_collector_matches_store_state()
    {
        // Stream the captured Synergy box session through a temp file as if SC were writing it.
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "synergy-box-session.log");
        var fixtureLines = File.ReadAllLines(fixturePath);

        using var log = new TempLog();
        var (store, conn) = NewStore();
        using (conn)
        {
            var collector = new GameLogCollector(
                new LogTailer(log.Path),
                new EventApplier(store, new ItemNameResolver()));

            // Drip-feed in two batches to exercise the streaming path.
            var half = fixtureLines.Length / 2;
            log.Append(fixtureLines.Take(half).ToArray());
            collector.Tick();
            log.Append(fixtureLines.Skip(half).ToArray());
            collector.Tick();

            var item = store.GetItem("Drink_bottle_synergy_01_plus_a")!;
            Assert.Equal(2, store.GetHolding(601563981557, item.Id)!.Quantity);
            Assert.NotNull(store.GetLocation(2900774186));
        }
    }
}
