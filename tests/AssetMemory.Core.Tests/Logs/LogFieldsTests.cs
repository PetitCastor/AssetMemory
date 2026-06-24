using AssetMemory.Core.Logs;

namespace AssetMemory.Core.Tests.Logs;

public class LogFieldsTests
{
    [Fact]
    public void Gets_simple_value()
    {
        Assert.Equal("Arcadiius", LogFields.Get("Player[Arcadiius] Type[Move]", "Player"));
    }

    [Fact]
    public void Gets_second_field()
    {
        Assert.Equal("Move", LogFields.Get("Player[Arcadiius] Type[Move]", "Type"));
    }

    [Fact]
    public void Value_can_contain_spaces_and_commas()
    {
        Assert.Equal("a, b, 123", LogFields.Get("Attachment[a, b, 123] Status[ok]", "Attachment"));
    }

    [Fact]
    public void Empty_brackets_yield_empty_string_not_null()
    {
        Assert.True(LogFields.TryGet("ItemClass[] Foo[1]", "ItemClass", out var value));
        Assert.Equal("", value);
    }

    [Fact]
    public void Does_not_match_key_that_is_a_suffix_of_a_larger_word()
    {
        // "Inventory" must not match inside "SourceInventory"
        Assert.Null(LogFields.Get("SourceInventory[601563981557:Container:0]", "Inventory"));
    }

    [Fact]
    public void Matches_multi_word_key_with_space()
    {
        Assert.Equal("601563981557:Container:0",
            LogFields.Get("Source Inventory[601563981557:Container:0] Target Inventory[INVALID]", "Source Inventory"));
    }

    [Fact]
    public void Takes_first_occurrence()
    {
        Assert.Equal("2", LogFields.Get("Source[x] amount[2] Target[y] amount[0]", "amount"));
    }

    [Fact]
    public void Missing_key_returns_null_and_false()
    {
        Assert.False(LogFields.TryGet("Player[Arcadiius]", "Nope", out var value));
        Assert.Null(value);
        Assert.Null(LogFields.Get("Player[Arcadiius]", "Nope"));
    }
}
