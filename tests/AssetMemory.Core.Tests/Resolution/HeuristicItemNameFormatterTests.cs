using AssetMemory.Core.Resolution;

namespace AssetMemory.Core.Tests.Resolution;

public class HeuristicItemNameFormatterTests
{
    private readonly HeuristicItemNameFormatter _formatter = new();

    [Fact]
    public void Replaces_underscores_with_spaces()
        => Assert.Equal("Drink bottle synergy 01 plus a",
            _formatter.Format("Drink_bottle_synergy_01_plus_a"));

    [Fact]
    public void Capitalizes_first_letter_when_first_segment_is_lowercase()
        => Assert.Equal("Rsi odyssey undersuit 01 01 01",
            _formatter.Format("rsi_odyssey_undersuit_01_01_01"));

    [Fact]
    public void Preserves_internal_casing_of_each_segment()
        => Assert.Equal("Carryable TBO InventoryContainer 2SCU",
            _formatter.Format("Carryable_TBO_InventoryContainer_2SCU"));

    [Fact]
    public void Single_segment_is_capitalized()
        => Assert.Equal("Backpack", _formatter.Format("backpack"));

    [Fact]
    public void Already_capitalized_first_segment_unchanged()
        => Assert.Equal("Drink bottle", _formatter.Format("Drink_bottle"));

    [Fact]
    public void Numeric_segments_pass_through()
        => Assert.Equal("Item 01 02 03", _formatter.Format("item_01_02_03"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_or_whitespace_returns_empty(string? input)
        => Assert.Equal("", _formatter.Format(input));

    [Fact]
    public void Collapses_consecutive_underscores()
        => Assert.Equal("Foo bar", _formatter.Format("foo__bar"));

    [Fact]
    public void Trims_leading_and_trailing_underscores()
        => Assert.Equal("Foo bar", _formatter.Format("_foo_bar_"));
}
