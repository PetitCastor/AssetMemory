using AssetMemory.Core.Detection;

namespace AssetMemory.Core.Tests;

public class AppPathsTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ampaths_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void MigrateLegacyData_copies_db_sidecars_and_settings_into_an_empty_data_dir()
    {
        var legacy = TempDir();
        var data = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(legacy, "assetmemory.db"), "db");
            File.WriteAllText(Path.Combine(legacy, "assetmemory.db-wal"), "wal");
            File.WriteAllText(Path.Combine(legacy, "assetmemory.db-shm"), "shm");
            File.WriteAllText(Path.Combine(legacy, "settings.json"), "{}");

            AppPaths.MigrateLegacyData(data, legacy);

            Assert.Equal("db", File.ReadAllText(Path.Combine(data, "assetmemory.db")));
            Assert.Equal("wal", File.ReadAllText(Path.Combine(data, "assetmemory.db-wal")));
            Assert.Equal("shm", File.ReadAllText(Path.Combine(data, "assetmemory.db-shm")));
            Assert.Equal("{}", File.ReadAllText(Path.Combine(data, "settings.json")));
        }
        finally
        {
            Directory.Delete(legacy, true);
            Directory.Delete(data, true);
        }
    }

    [Fact]
    public void MigrateLegacyData_is_a_no_op_when_the_data_dir_already_has_a_db()
    {
        var legacy = TempDir();
        var data = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(legacy, "assetmemory.db"), "legacy");
            File.WriteAllText(Path.Combine(data, "assetmemory.db"), "existing");

            AppPaths.MigrateLegacyData(data, legacy);

            // Existing user data is never clobbered by a stale copy left beside the exe.
            Assert.Equal("existing", File.ReadAllText(Path.Combine(data, "assetmemory.db")));
        }
        finally
        {
            Directory.Delete(legacy, true);
            Directory.Delete(data, true);
        }
    }

    [Fact]
    public void MigrateLegacyData_is_a_no_op_when_there_is_no_legacy_db()
    {
        var legacy = TempDir();
        var data = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(legacy, "settings.json"), "{}");

            AppPaths.MigrateLegacyData(data, legacy);

            // Without a legacy DB there is nothing to migrate — settings alone don't trigger a copy.
            Assert.False(File.Exists(Path.Combine(data, "settings.json")));
        }
        finally
        {
            Directory.Delete(legacy, true);
            Directory.Delete(data, true);
        }
    }
}
