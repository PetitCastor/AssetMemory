using AssetMemory.Core.Inventory;

namespace AssetMemory.Core.Tests.Inventory;

public class InventoryRefTests
{
    [Fact]
    public void Parses_location_ref()
    {
        Assert.True(InventoryRef.TryParse("204821708183:Location:2900774186", out var r));
        Assert.Equal(204821708183, r.Owner);
        Assert.Equal(InventoryKind.Location, r.Kind);
        Assert.Equal(2900774186, r.Id);
    }

    [Fact]
    public void Parses_container_ref()
    {
        Assert.True(InventoryRef.TryParse("601563981557:Container:0", out var r));
        Assert.Equal(601563981557, r.Owner);
        Assert.Equal(InventoryKind.Container, r.Kind);
        Assert.Equal(0, r.Id);
    }

    [Fact]
    public void Parses_client_only_ref()
    {
        Assert.True(InventoryRef.TryParse("0:ClientOnly:1", out var r));
        Assert.Equal(InventoryKind.ClientOnly, r.Kind);
    }

    [Fact]
    public void Unknown_kind_still_parses_with_unknown_enum()
    {
        Assert.True(InventoryRef.TryParse("5:Weird:7", out var r));
        Assert.Equal(InventoryKind.Unknown, r.Kind);
        Assert.Equal(5, r.Owner);
        Assert.Equal(7, r.Id);
    }

    [Fact]
    public void Preserves_raw_text()
    {
        Assert.True(InventoryRef.TryParse("601563981557:Container:0", out var r));
        Assert.Equal("601563981557:Container:0", r.Raw);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("INVALID")]
    [InlineData("NULL")]
    [InlineData("garbage")]
    [InlineData("1:Container")]
    [InlineData("a:Container:0")]
    [InlineData("1:Container:b")]
    public void Rejects_non_refs(string? text)
    {
        Assert.False(InventoryRef.TryParse(text, out var r));
        Assert.Equal(default, r);
    }
}
