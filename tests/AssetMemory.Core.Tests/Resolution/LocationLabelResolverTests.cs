using AssetMemory.Core.Resolution;

namespace AssetMemory.Core.Tests.Resolution;

public class LocationLabelResolverTests
{
    [Fact]
    public void TryResolve_returns_false_for_unknown_id()
    {
        var resolver = new LocationLabelResolver();
        Assert.False(resolver.TryResolve(123, out var label));
        Assert.Null(label);
    }

    [Fact]
    public void TryResolve_returns_label_after_Set()
    {
        var resolver = new LocationLabelResolver();
        resolver.Set(2900774186, "Aaron Halo SCU Box");
        Assert.True(resolver.TryResolve(2900774186, out var label));
        Assert.Equal("Aaron Halo SCU Box", label);
    }

    [Fact]
    public void Set_overwrites_existing_label()
    {
        var resolver = new LocationLabelResolver();
        resolver.Set(1, "first");
        resolver.Set(1, "second");
        Assert.Equal("second", resolver.ResolveOrFallback(1));
    }

    [Fact]
    public void ResolveOrFallback_returns_synthetic_label_for_unknown_id()
        => Assert.Equal("Location 2900774186",
            new LocationLabelResolver().ResolveOrFallback(2900774186));

    [Fact]
    public void Bulk_constructor_seeds_labels()
    {
        var resolver = new LocationLabelResolver(new Dictionary<long, string>
        {
            [1] = "one",
            [2] = "two",
        });
        Assert.Equal("one", resolver.ResolveOrFallback(1));
        Assert.Equal("two", resolver.ResolveOrFallback(2));
    }

    [Fact]
    public void KnownIds_includes_both_seeded_and_set_entries()
    {
        var resolver = new LocationLabelResolver(new Dictionary<long, string> { [1] = "a" });
        resolver.Set(2, "b");
        Assert.Equal(new long[] { 1, 2 }, resolver.KnownIds.OrderBy(x => x));
    }

    [Fact]
    public void Set_rejects_null_or_empty_label()
    {
        var resolver = new LocationLabelResolver();
        Assert.Throws<ArgumentException>(() => resolver.Set(1, ""));
        Assert.Throws<ArgumentException>(() => resolver.Set(1, "   "));
    }

    [Fact]
    public void Remove_clears_a_label()
    {
        var resolver = new LocationLabelResolver();
        resolver.Set(1, "x");
        Assert.True(resolver.Remove(1));
        Assert.False(resolver.TryResolve(1, out _));
    }

    [Fact]
    public void Remove_returns_false_when_id_was_unknown()
        => Assert.False(new LocationLabelResolver().Remove(99));
}
