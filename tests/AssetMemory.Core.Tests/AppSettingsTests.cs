using AssetMemory.Core.Detection;

namespace AssetMemory.Core.Tests;

public class AppSettingsTests
{
    [Fact]
    public void SyncInceptionUtc_round_trips_through_save_and_load()
    {
        var path = Path.Combine(Path.GetTempPath(), $"amsettings_{Guid.NewGuid():N}.json");
        try
        {
            var inception = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
            new AppSettings { GameLogPath = @"E:\SC\Game.log", SyncInceptionUtc = inception }.Save(path);

            var loaded = AppSettings.Load(path);

            Assert.Equal(inception, loaded.SyncInceptionUtc);
            Assert.Equal(@"E:\SC\Game.log", loaded.GameLogPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SyncInceptionUtc_defaults_to_null_when_absent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"amsettings_{Guid.NewGuid():N}.json");
        try
        {
            new AppSettings { GameLogPath = @"E:\SC\Game.log" }.Save(path);

            var loaded = AppSettings.Load(path);

            Assert.Null(loaded.SyncInceptionUtc);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
