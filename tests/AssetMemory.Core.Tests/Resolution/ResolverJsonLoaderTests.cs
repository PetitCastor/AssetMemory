using System.Text;
using AssetMemory.Core.Resolution;

namespace AssetMemory.Core.Tests.Resolution;

public class ResolverJsonLoaderTests
{
    // ---------- item overrides ----------

    [Fact]
    public void Loads_item_overrides_from_json_object()
    {
        var json = """{"Drink_bottle_synergy_01_plus_a": "Synergy+ Bottle"}""";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var overrides = ResolverJsonLoader.LoadItemOverrides(ms);
        Assert.Equal("Synergy+ Bottle", overrides["Drink_bottle_synergy_01_plus_a"]);
    }

    [Fact]
    public void Loads_empty_object_as_empty_dictionary()
    {
        using var ms = new MemoryStream("{}"u8.ToArray());
        Assert.Empty(ResolverJsonLoader.LoadItemOverrides(ms));
    }

    [Fact]
    public void Item_overrides_round_trip()
    {
        var input = new Dictionary<string, string>
        {
            ["foo_bar"] = "Foo Bar",
            ["alpha_beta"] = "Alpha β",
        };
        using var save = new MemoryStream();
        ResolverJsonLoader.SaveItemOverrides(input, save);
        save.Position = 0;
        var loaded = ResolverJsonLoader.LoadItemOverrides(save);
        Assert.Equal(input.Count, loaded.Count);
        foreach (var (k, v) in input) Assert.Equal(v, loaded[k]);
    }

    [Fact]
    public void Item_overrides_saved_in_sorted_key_order()
    {
        var input = new Dictionary<string, string>
        {
            ["zebra"] = "Z",
            ["alpha"] = "A",
            ["mango"] = "M",
        };
        using var ms = new MemoryStream();
        ResolverJsonLoader.SaveItemOverrides(input, ms);
        var json = Encoding.UTF8.GetString(ms.ToArray());
        var ia = json.IndexOf("\"alpha\"", StringComparison.Ordinal);
        var im = json.IndexOf("\"mango\"", StringComparison.Ordinal);
        var iz = json.IndexOf("\"zebra\"", StringComparison.Ordinal);
        Assert.True(ia < im && im < iz, $"expected alpha<mango<zebra; got {ia},{im},{iz}");
    }

    [Fact]
    public void Save_produces_indented_json_for_human_editing()
    {
        var input = new Dictionary<string, string> { ["a"] = "A", ["b"] = "B" };
        using var ms = new MemoryStream();
        ResolverJsonLoader.SaveItemOverrides(input, ms);
        var json = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("\n", json);
    }

    // ---------- location labels ----------

    [Fact]
    public void Loads_location_labels_with_numeric_string_keys()
    {
        var json = """{"2900774186": "Aaron Halo SCU Box"}""";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var labels = ResolverJsonLoader.LoadLocationLabels(ms);
        Assert.Equal("Aaron Halo SCU Box", labels[2900774186]);
    }

    [Fact]
    public void Location_labels_round_trip()
    {
        var input = new Dictionary<long, string>
        {
            [2900774186] = "Aaron Halo",
            [12345] = "Hangar A",
        };
        using var save = new MemoryStream();
        ResolverJsonLoader.SaveLocationLabels(input, save);
        save.Position = 0;
        var loaded = ResolverJsonLoader.LoadLocationLabels(save);
        Assert.Equal(input.Count, loaded.Count);
        foreach (var (k, v) in input) Assert.Equal(v, loaded[k]);
    }

    [Fact]
    public void Throws_helpful_error_on_malformed_json()
    {
        using var ms = new MemoryStream("not json"u8.ToArray());
        Assert.Throws<InvalidDataException>(() => ResolverJsonLoader.LoadItemOverrides(ms));
    }

    [Fact]
    public void Throws_helpful_error_when_location_key_is_not_numeric()
    {
        var json = """{"not-a-number": "label"}""";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
        Assert.Throws<InvalidDataException>(() => ResolverJsonLoader.LoadLocationLabels(ms));
    }
}
