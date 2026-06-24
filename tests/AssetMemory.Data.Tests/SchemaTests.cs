using AssetMemory.Data;
using Microsoft.Data.Sqlite;

namespace AssetMemory.Data.Tests;

public class SchemaTests
{
    [Fact]
    public void ApplyMigration_creates_all_expected_tables()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            var tables = ListTables(conn);
            Assert.Contains("locations", tables);
            Assert.Contains("items", tables);
            Assert.Contains("holdings", tables);
            Assert.Contains("equipped", tables);
            Assert.Contains("events_audit", tables);
        }
    }

    [Fact]
    public void ApplyMigration_is_idempotent()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            store.ApplyMigration();
            store.ApplyMigration();
            Assert.Contains("locations", ListTables(conn));
        }
    }

    [Fact]
    public void Foreign_keys_are_enabled_on_the_connection()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys;";
            Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
        }
    }

    [Fact]
    public void Schema_version_is_recorded()
    {
        var (store, conn) = TestStore.CreateMigrated();
        using (conn)
        {
            Assert.True(store.SchemaVersion >= 1);
        }
    }

    private static List<string> ListTables(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
        using var reader = cmd.ExecuteReader();
        var list = new List<string>();
        while (reader.Read()) list.Add(reader.GetString(0));
        return list;
    }
}
