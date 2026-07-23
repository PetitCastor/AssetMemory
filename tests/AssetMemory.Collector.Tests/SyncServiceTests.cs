using AssetMemory.Core.Detection;
using AssetMemory.Core.Resolution;
using AssetMemory.Data;
using AssetMemory.Data.Events;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssetMemory.Collector.Tests;

public class SyncServiceTests
{
    private sealed class TempGameDir : IDisposable
    {
        public string Dir { get; }
        public string LogPath { get; }
        public string BackupDir { get; }

        public TempGameDir()
        {
            Dir = Path.Combine(Path.GetTempPath(), $"am-sync-{Guid.NewGuid():N}");
            BackupDir = Path.Combine(Dir, "logbackups");
            Directory.CreateDirectory(BackupDir);
            LogPath = Path.Combine(Dir, "Game.log");
        }

        public void Dispose()
        {
            try { Directory.Delete(Dir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static void Write(string path, params string[] lines)
        => File.WriteAllText(path, string.Concat(lines.Select(l => l + "\r\n")));

    private static SyncService NewSync(TempGameDir g, out AssetMemoryStore store, out AppSettings settings)
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        store = new AssetMemoryStore(conn);
        store.ApplyMigration();
        var collector = new GameLogCollector(new LogTailer(g.LogPath), new EventApplier(store, new ItemNameResolver()));
        settings = new AppSettings { GameLogPath = g.LogPath };
        return new SyncService(collector, settings, Path.Combine(g.Dir, "settings.json"), NullLogger<SyncService>.Instance);
    }

    [Fact]
    public void Sync_replays_older_backups_before_the_newer_current_log_so_a_later_equip_out_debits()
    {
        // Regression: the box kept an item that had been equipped straight out of it, because Sync
        // processed the current (newest) log before the older backups. The equip-out debit ran first
        // against an empty box (clamped to zero, no-op), then the older backup credit re-added it.
        using var g = new TempGameDir();

        // OLDER backup session: the helmet is moved into box 999.
        Write(Path.Combine(g.BackupDir, "Game Build(1) 18 Jul 26 (10 00 00).log"),
            "<2026-07-18T10:00:00.000Z> [Notice] <InventoryManagementRequest> Queued Request[5] Type[Move] for 'Arcadiius' [1] Source Inventory[11:Container:0] Target Inventory[999:Container:0]. Source[helmet_x] amount[1] rank[a]. Target[NULL] amount[0] rank[b]. Item[NONE] action[None]. [x]");

        // NEWER current log: the helmet is equipped straight out of box 999.
        Write(g.LogPath,
            "<2026-07-23T11:00:00.000Z> [Notice] <EquipItem> Request[9] equip from Inventory[999:Container:0] Class[helmet_x] Rank[z] Port[Armor_Helmet] DependentRequest[0] PostAction[None] [x]");

        var sync = NewSync(g, out var store, out var settings);
        sync.Sync();

        var item = store.GetItem("helmet_x")!;
        Assert.Null(store.GetHolding(999, item.Id));  // credited (older) then debited (newer) in order -> gone
        Assert.Contains("Game Build(1) 18 Jul 26 (10 00 00).log", settings.ProcessedBackups);
    }

    [Fact]
    public void Sync_orders_multiple_backups_by_first_timestamp_not_filename()
    {
        // Cross-month filenames don't sort chronologically as strings ("Jul" < "Mar"), so ordering must
        // key off each file's first log timestamp. Credit in March, debit in July -> ends empty.
        using var g = new TempGameDir();
        Write(g.LogPath, "<2026-08-01T00:00:00.000Z> [Notice] <GetGridItem> Request[1] Number of Items[0] in Inventories[0] [x]"); // inert newest

        Write(Path.Combine(g.BackupDir, "Game Build(9) 20 Jul 26 (10 00 00).log"),
            "<2026-07-20T10:00:00.000Z> [Notice] <EquipItem> Request[9] equip from Inventory[999:Container:0] Class[widget] Rank[z] Port[p] DependentRequest[0] PostAction[None] [x]");
        Write(Path.Combine(g.BackupDir, "Game Build(2) 05 Mar 26 (09 00 00).log"),
            "<2026-03-05T09:00:00.000Z> [Notice] <InventoryManagementRequest> Queued Request[5] Type[Move] for 'A' [1] Source Inventory[11:Container:0] Target Inventory[999:Container:0]. Source[widget] amount[1] rank[a]. Target[NULL] amount[0] rank[b]. Item[NONE] action[None]. [x]");

        var sync = NewSync(g, out var store, out _);
        sync.Sync();

        var item = store.GetItem("widget")!;
        Assert.Null(store.GetHolding(999, item.Id));  // March credit applied before July debit despite filename order
    }
}
