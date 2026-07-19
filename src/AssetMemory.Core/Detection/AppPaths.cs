namespace AssetMemory.Core.Detection;

/// <summary>
/// Resolves where AssetMemory keeps its user data — the SQLite DB and <c>settings.json</c>. Data lives
/// in <c>%LOCALAPPDATA%\AssetMemory\</c> so it survives redeploys (which overwrite the exe folder) and
/// so long-term "memories" are decoupled from the app install. On first run after the move it migrates a
/// legacy install's data — the db plus its WAL/SHM sidecars and settings.json that older builds kept
/// next to the exe — into the new directory.
/// </summary>
public static class AppPaths
{
    private static readonly string[] DataFiles =
        ["assetmemory.db", "assetmemory.db-wal", "assetmemory.db-shm", "settings.json"];

    /// <summary><c>%LOCALAPPDATA%\AssetMemory</c> (created on demand by <see cref="EnsureReady"/>).</summary>
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AssetMemory");

    public static string SettingsPath => Path.Combine(DataDir, "settings.json");
    public static string DbPath => Path.Combine(DataDir, "assetmemory.db");

    /// <summary>
    /// Ensures <see cref="DataDir"/> exists and, on the first run after the move, copies a legacy
    /// next-to-exe install's data in. Safe to call repeatedly.
    /// </summary>
    public static void EnsureReady()
    {
        Directory.CreateDirectory(DataDir);
        MigrateLegacyData(DataDir, AppContext.BaseDirectory);
    }

    /// <summary>
    /// Copies a legacy install's data (db + WAL/SHM + settings.json) from <paramref name="legacyDir"/>
    /// into <paramref name="dataDir"/>, but only when <paramref name="dataDir"/> has no db yet — so a
    /// populated user-folder DB is never clobbered by a stale copy left beside the exe. Exposed for
    /// testing with arbitrary directories; production reaches it via <see cref="EnsureReady"/>.
    /// </summary>
    public static void MigrateLegacyData(string dataDir, string legacyDir)
    {
        if (File.Exists(Path.Combine(dataDir, "assetmemory.db")))
            return;
        if (!File.Exists(Path.Combine(legacyDir, "assetmemory.db")))
            return;

        foreach (var name in DataFiles)
        {
            var src = Path.Combine(legacyDir, name);
            var dst = Path.Combine(dataDir, name);
            if (File.Exists(src) && !File.Exists(dst))
                File.Copy(src, dst);
        }
    }
}
