using AssetMemory.Core.Logs;

namespace AssetMemory.Core.Tests.Logs;

public class LogEntryParserTests
{
    private const string SampleLine =
        "<2026-06-24T19:22:39.827Z> [Notice] <InventoryManagement> New request[22] Player[Arcadiius] Type[OpenNestedInventory] [Team_CoreGameplayFeatures][Inventory]";

    [Fact]
    public void Parses_severity()
    {
        Assert.True(LogEntryParser.TryParse(SampleLine, out var entry));
        Assert.Equal("Notice", entry!.Severity);
    }

    [Fact]
    public void Parses_category()
    {
        Assert.True(LogEntryParser.TryParse(SampleLine, out var entry));
        Assert.Equal("InventoryManagement", entry!.Category);
    }

    [Fact]
    public void Message_excludes_the_envelope()
    {
        Assert.True(LogEntryParser.TryParse(SampleLine, out var entry));
        Assert.StartsWith("New request[22] Player[Arcadiius]", entry!.Message);
        Assert.DoesNotContain("<InventoryManagement>", entry.Message);
        Assert.DoesNotContain("[Notice]", entry.Message);
    }

    [Fact]
    public void Parses_timestamp_as_utc()
    {
        Assert.True(LogEntryParser.TryParse(SampleLine, out var entry));
        Assert.Equal(TimeSpan.Zero, entry!.Timestamp.Offset);
        Assert.Equal(new DateTime(2026, 6, 24, 19, 22, 39, 827, DateTimeKind.Utc), entry.Timestamp.UtcDateTime);
    }

    [Fact]
    public void Preserves_raw_line()
    {
        Assert.True(LogEntryParser.TryParse(SampleLine, out var entry));
        Assert.Equal(SampleLine, entry!.Raw);
    }

    [Fact]
    public void Parses_category_containing_spaces()
    {
        const string line =
            "<2026-06-24T19:23:50.563Z> [Notice] <Remove Inventory Container UI> Player[Arcadiius] inventory [204821708183:Location:2900774186] removed from UI [Team_CoreGameplayFeatures][Inventory]";
        Assert.True(LogEntryParser.TryParse(line, out var entry));
        Assert.Equal("Remove Inventory Container UI", entry!.Category);
        Assert.StartsWith("Player[Arcadiius] inventory", entry.Message);
    }

    [Fact]
    public void Trims_trailing_carriage_return()
    {
        Assert.True(LogEntryParser.TryParse(SampleLine + "\r", out var entry));
        Assert.EndsWith("[Inventory]", entry!.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a log line at all")]
    [InlineData("<2026-06-24T19:22:39.827Z> missing brackets")]
    [InlineData("<not-a-timestamp> [Notice] <Cat> body")]
    public void Rejects_malformed_lines(string? line)
    {
        Assert.False(LogEntryParser.TryParse(line, out var entry));
        Assert.Null(entry);
    }
}
