using AssetMemory.Core.Resolution;

namespace AssetMemory.Core.Tests.Resolution;

public class StationNameResolverTests
{
    // ---------- curated overrides (the default dictionary) ----------

    [Theory]
    [InlineData("Stanton4_NewBabbage", "New Babbage")]
    [InlineData("Stanton1_Lorville", "Lorville")]
    [InlineData("Stanton3_Area18", "Area18")]
    [InlineData("Stanton2_Orison", "Orison")]
    [InlineData("Nyx_Levski", "Levski")]
    [InlineData("Nyx_Kaboos", "Kaboos")]
    [InlineData("RR_HUR_LEO", "Everus Harbor")]
    [InlineData("RR_MIC_LEO", "Port Tressler")]
    [InlineData("RR_ARC_LEO", "Baijini Point")]
    [InlineData("RR_CRU_LEO", "Seraphim Station")]
    public void Resolve_uses_curated_name_for_known_hubs(string code, string expected)
        => Assert.Equal(expected, new StationNameResolver().Resolve(code));

    // ---------- heuristic fallback (the long tail) ----------

    [Theory]
    [InlineData("RR_HUR_L2", "Hurston L2")]
    [InlineData("RR_ARC_L1", "ArcCorp L1")]
    [InlineData("RR_CRU_L4", "Crusader L4")]
    [InlineData("RR_P2_L4", "Pyro 2 L4")]
    [InlineData("RR_JP_NyxPyro", "Nyx Pyro Jump Point")]
    [InlineData("Stanton4_DistributionCentre_Covalex_S4DC05", "Distribution Centre Covalex S4DC05")]
    [InlineData("Stanton1_HurdynMining_HDMSStanhope", "Hurdyn Mining HDMS Stanhope")]
    public void Resolve_falls_back_to_a_readable_heuristic(string code, string expected)
        => Assert.Equal(expected, new StationNameResolver().Resolve(code));

    [Fact]
    public void Resolve_of_null_or_empty_is_empty()
    {
        Assert.Equal("", new StationNameResolver().Resolve(null));
        Assert.Equal("", new StationNameResolver().Resolve(""));
    }

    [Fact]
    public void Injected_overrides_take_precedence_over_defaults_and_heuristic()
    {
        var resolver = new StationNameResolver(new Dictionary<string, string>
        {
            ["RR_HUR_L2"] = "Green Glade Station",
        });
        Assert.Equal("Green Glade Station", resolver.Resolve("RR_HUR_L2"));
    }

    // ---------- formatter in isolation ----------

    [Theory]
    [InlineData("RR_MIC_L2", "microTech L2")]
    [InlineData("Stanton3a_Shubin_SAL2", "Shubin SAL2")]
    [InlineData("Nyx_TSG_QVExtractionStation_033", "Nyx TSG QV Extraction Station 033")]
    public void Heuristic_formatter_prettifies_codes(string code, string expected)
        => Assert.Equal(expected, new HeuristicStationNameFormatter().Format(code));
}
