using AssetMemory.Core.Resolution;

namespace AssetMemory.Core.Tests.Resolution;

public class ItemNameResolverTests
{
    [Fact]
    public void Override_takes_precedence_over_heuristic()
    {
        var resolver = new ItemNameResolver(new Dictionary<string, string>
        {
            ["Drink_bottle_synergy_01_plus_a"] = "Synergy+ Bottle",
        });
        Assert.Equal("Synergy+ Bottle", resolver.Resolve("Drink_bottle_synergy_01_plus_a"));
    }

    [Fact]
    public void Falls_back_to_heuristic_when_no_override()
    {
        var resolver = new ItemNameResolver();
        Assert.Equal("Klwe pistol energy 01", resolver.Resolve("klwe_pistol_energy_01"));
    }

    [Fact]
    public void Empty_overrides_still_falls_back()
    {
        var resolver = new ItemNameResolver(new Dictionary<string, string>());
        Assert.Equal("Some unknown thing", resolver.Resolve("some_unknown_thing"));
    }

    [Fact]
    public void Override_lookup_is_case_sensitive_matching_log_classes()
    {
        // Log classes are case-stable; we don't want fuzzy collisions between variants.
        var resolver = new ItemNameResolver(new Dictionary<string, string> { ["foo_bar"] = "Foo" });
        Assert.Equal("Foo", resolver.Resolve("foo_bar"));
        Assert.NotEqual("Foo", resolver.Resolve("Foo_bar"));
    }

    [Fact]
    public void HasOverride_reports_curated_entries()
    {
        var resolver = new ItemNameResolver(new Dictionary<string, string> { ["x"] = "X" });
        Assert.True(resolver.HasOverride("x"));
        Assert.False(resolver.HasOverride("y"));
    }

    [Fact]
    public void Empty_input_resolves_to_empty()
    {
        Assert.Equal("", new ItemNameResolver().Resolve(""));
    }

    [Fact]
    public void Custom_formatter_is_used_as_fallback()
    {
        var resolver = new ItemNameResolver(
            overrides: null,
            fallback: new ConstantFormatter("UNKNOWN"));
        Assert.Equal("UNKNOWN", resolver.Resolve("anything"));
    }

    private sealed class ConstantFormatter(string value) : IItemNameFormatter
    {
        public string Format(string? itemClass) => value;
    }
}
