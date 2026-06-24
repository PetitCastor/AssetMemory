using AssetMemory.Collector;

namespace AssetMemory.Collector.Tests;

public class LogTailerTests
{
    [Fact]
    public void Empty_file_yields_no_lines()
    {
        using var log = new TempLog();
        using var tailer = new LogTailer(log.Path);
        Assert.Empty(tailer.ReadNew());
    }

    [Fact]
    public void Reads_existing_lines_on_first_call()
    {
        using var log = new TempLog();
        log.Append("alpha", "beta", "gamma");

        using var tailer = new LogTailer(log.Path);
        Assert.Equal(["alpha", "beta", "gamma"], tailer.ReadNew());
    }

    [Fact]
    public void Returns_only_new_lines_on_second_call()
    {
        using var log = new TempLog();
        log.Append("a", "b");

        using var tailer = new LogTailer(log.Path);
        _ = tailer.ReadNew().ToList();

        log.Append("c", "d");
        Assert.Equal(["c", "d"], tailer.ReadNew());
    }

    [Fact]
    public void Returns_empty_when_nothing_new_was_written()
    {
        using var log = new TempLog();
        log.Append("a");

        using var tailer = new LogTailer(log.Path);
        _ = tailer.ReadNew().ToList();

        Assert.Empty(tailer.ReadNew());
        Assert.Empty(tailer.ReadNew());
    }

    [Fact]
    public void Truncation_replays_from_zero()
    {
        using var log = new TempLog();
        log.Append("old-1", "old-2", "old-3");

        using var tailer = new LogTailer(log.Path);
        _ = tailer.ReadNew().ToList();

        // Game launches: SC truncates Game.log to 0 then writes fresh lines.
        log.Truncate();
        log.Append("fresh-1", "fresh-2");

        Assert.Equal(["fresh-1", "fresh-2"], tailer.ReadNew());
    }

    [Fact]
    public void Partial_line_without_terminator_is_held_until_complete()
    {
        using var log = new TempLog();
        log.Append("complete-line");
        File.AppendAllText(log.Path, "partial-without-newline");

        using var tailer = new LogTailer(log.Path);
        Assert.Equal(["complete-line"], tailer.ReadNew());

        // Tailer's position must NOT have advanced past the partial line — when it completes,
        // we want the FULL line, not just the second half.
        File.AppendAllText(log.Path, "-now-complete\r\n");
        Assert.Equal(["partial-without-newline-now-complete"], tailer.ReadNew());
    }

    [Fact]
    public void Missing_file_yields_no_lines_until_it_exists()
    {
        var path = Path.Combine(Path.GetTempPath(), $"assetmemory-missing-{Guid.NewGuid():N}.log");
        using var tailer = new LogTailer(path);
        Assert.Empty(tailer.ReadNew());

        File.WriteAllText(path, "hello\r\nworld\r\n");
        try
        {
            Assert.Equal(["hello", "world"], tailer.ReadNew());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Reset_makes_the_next_read_replay_everything()
    {
        using var log = new TempLog();
        log.Append("a", "b");
        using var tailer = new LogTailer(log.Path);
        _ = tailer.ReadNew().ToList();

        tailer.Reset();

        Assert.Equal(["a", "b"], tailer.ReadNew());
    }

    [Fact]
    public void Position_advances_to_end_of_last_complete_line()
    {
        using var log = new TempLog();
        log.Append("xyz");
        using var tailer = new LogTailer(log.Path);
        _ = tailer.ReadNew().ToList();

        var written = new FileInfo(log.Path).Length;
        Assert.Equal(written, tailer.Position);
    }

    [Fact]
    public void Tailer_does_not_block_a_concurrent_writer()
    {
        using var log = new TempLog();
        using var tailer = new LogTailer(log.Path);

        // Hold a write handle for the duration of the read, like SC does.
        using var writer = new FileStream(log.Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        writer.Write(System.Text.Encoding.UTF8.GetBytes("first\r\n"));
        writer.Flush();

        Assert.Equal(["first"], tailer.ReadNew());

        writer.Write(System.Text.Encoding.UTF8.GetBytes("second\r\n"));
        writer.Flush();

        Assert.Equal(["second"], tailer.ReadNew());
    }
}
