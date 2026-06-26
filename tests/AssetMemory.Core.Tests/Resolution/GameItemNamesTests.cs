using AssetMemory.Core.Resolution;

namespace AssetMemory.Core.Tests.Resolution;

public class GameItemNamesTests
{
    // Real-shaped lines from the game's data/Localization/english/global.ini.
    private static readonly string[] Lines =
    [
        "[General]",
        "item_Namebehr_smg_ballistic_01=P8-SC SMG",
        "item_Namebehr_smg_ballistic_01_white02=P8-SC \"Boneyard\" SMG",
        "item_Namebehr_smg_ballistic_01_white02_short=P8-SC SMG",  // _short variant must be skipped
        "item_Descbehr_smg_ballistic_01=Manufacturer: Behring...",  // descriptions are not names
        "item_Nameempty_thing=",                                    // empty value skipped
        "unrelated_key=value",
    ];

    [Fact]
    public void Parse_maps_item_name_keys_to_display_names()
    {
        var map = GameItemNames.Parse(Lines);

        Assert.Equal("P8-SC SMG", map["behr_smg_ballistic_01"]);
        Assert.Equal("P8-SC \"Boneyard\" SMG", map["behr_smg_ballistic_01_white02"]);
    }

    [Fact]
    public void Parse_skips_short_variants_descriptions_and_empty_values()
    {
        var map = GameItemNames.Parse(Lines);

        Assert.False(map.ContainsKey("behr_smg_ballistic_01_white02_short"));
        Assert.False(map.ContainsKey("empty_thing"));
        Assert.DoesNotContain(map.Values, v => v.StartsWith("Manufacturer"));
    }

    [Fact]
    public void Parse_normalises_the_armor_underscore_key_form_to_the_log_class()
    {
        // Weapons use item_Name<class>; armor/clothing use item_Name_<class>. The log emits the
        // class with no leading underscore either way, so both must resolve.
        var map = GameItemNames.Parse(["item_Name_qrt_combat_heavy_arms_02_01_01=Bokto Arms"]);
        Assert.Equal("Bokto Arms", map["qrt_combat_heavy_arms_02_01_01"]);
    }

    [Fact]
    public void Lookup_is_case_insensitive_to_match_log_class_casing()
    {
        // The log can emit a class in different casing than the ini key (e.g. IAE2022 vs iae2022).
        var map = GameItemNames.Parse(["item_Namebehr_smg_ballistic_01_IAE2022=P8-SC \"Red Alert\" SMG"]);
        Assert.Equal("P8-SC \"Red Alert\" SMG", map["behr_smg_ballistic_01_iae2022"]);
    }

    [Fact]
    public void Feeds_the_item_name_resolver_as_overrides()
    {
        var resolver = new ItemNameResolver(GameItemNames.Parse(Lines));

        Assert.Equal("P8-SC SMG", resolver.Resolve("behr_smg_ballistic_01"));
        // Unknown class still falls back to the heuristic formatter (only first segment capitalized).
        Assert.Equal("Unknown gizmo 99", resolver.Resolve("unknown_gizmo_99"));
    }
}
