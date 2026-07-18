using AssetMemory.Data;
using Microsoft.Data.Sqlite;

namespace AssetMemory.Tui;

/// <summary>
/// A dedicated read connection onto the shared SQLite file. Kept separate from the collector's write
/// connection (which lives in the background process, or in <see cref="AppHost"/> in sole mode) so
/// that WAL lets the UI's queries run concurrently with writes without single-connection threading
/// hazards. <c>busy_timeout</c> serializes the rare lock contention instead of throwing SQLITE_BUSY.
/// </summary>
public sealed class ReadStore : IDisposable
{
    private readonly SqliteConnection _conn;

    public AssetMemoryStore Store { get; }

    public ReadStore(string dbPath)
    {
        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA busy_timeout=3000;";
            cmd.ExecuteNonQuery();
        }
        Store = new AssetMemoryStore(_conn);
        Store.ApplyMigration(); // idempotent; guarantees schema exists before the first query
    }

    public void Dispose() => _conn.Dispose();
}
