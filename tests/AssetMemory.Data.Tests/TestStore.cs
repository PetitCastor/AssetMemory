using AssetMemory.Data;
using Microsoft.Data.Sqlite;

namespace AssetMemory.Data.Tests;

/// <summary>
/// Helper that opens an in-memory SQLite database, applies the schema, and hands back
/// a real <see cref="AssetMemoryStore"/> for the test to drive. The connection is owned
/// here so disposing the store also disposes it.
/// </summary>
internal static class TestStore
{
    public static (AssetMemoryStore store, SqliteConnection conn) CreateMigrated()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var store = new AssetMemoryStore(conn);
        store.ApplyMigration();
        return (store, conn);
    }
}
