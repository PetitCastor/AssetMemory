using AssetMemory.Core.Detection;

namespace AssetMemory.Core.Tests;

public class GamePathFinderTests
{
    [Fact]
    public void FindGameLogPaths_returns_empty_when_no_known_paths_exist()
    {
        var paths = GamePathFinder.FindGameLogPaths(["Z:\\"]);
        Assert.Empty(paths);
    }

    [Fact]
    public void FindGameLogPaths_finds_Game_log_under_StarCitizen_LIVE()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sctest_{Guid.NewGuid():N}");
        try
        {
            var liveDir = Path.Combine(root, "StarCitizen", "LIVE");
            Directory.CreateDirectory(liveDir);
            var logPath = Path.Combine(liveDir, "Game.log");
            File.WriteAllText(logPath, "test");

            var paths = GamePathFinder.FindGameLogPaths([root]);
            Assert.Single(paths);
            Assert.Equal(logPath, paths[0]);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void FindGameLogPaths_finds_multiple_channels()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sctest_{Guid.NewGuid():N}");
        try
        {
            foreach (var channel in new[] { "LIVE", "PTU", "EPTU" })
            {
                var dir = Path.Combine(root, "StarCitizen", channel);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "Game.log"), "test");
            }

            var paths = GamePathFinder.FindGameLogPaths([root]);
            Assert.Equal(3, paths.Count);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void FindGameLogPaths_searches_common_subdirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sctest_{Guid.NewGuid():N}");
        try
        {
            // Simulate: root\Games\StarCitizen\LIVE\Game.log
            var dir = Path.Combine(root, "Games", "StarCitizen", "LIVE");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "Game.log"), "test");

            var paths = GamePathFinder.FindGameLogPaths([root]);
            Assert.Single(paths);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void FindGameLogPaths_skips_directories_that_dont_exist()
    {
        var paths = GamePathFinder.FindGameLogPaths([@"Z:\nonexistent\path"]);
        Assert.Empty(paths);
    }

    [Fact]
    public void FindGameLogPaths_prefers_LIVE_over_PTU_and_EPTU()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sctest_{Guid.NewGuid():N}");
        try
        {
            foreach (var channel in new[] { "EPTU", "PTU", "LIVE" })
            {
                var dir = Path.Combine(root, "StarCitizen", channel);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "Game.log"), "test");
            }

            var paths = GamePathFinder.FindGameLogPaths([root]);
            Assert.Equal(3, paths.Count);
            Assert.Contains("LIVE", paths[0]);
            Assert.Contains("PTU", paths[1]);
            Assert.Contains("EPTU", paths[2]);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void IsValidGameLog_returns_true_for_existing_file()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            Assert.True(GamePathFinder.IsValidGameLog(tmp));
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void IsValidGameLog_returns_false_for_missing_file()
    {
        Assert.False(GamePathFinder.IsValidGameLog(@"Z:\no\such\file.log"));
    }
}
