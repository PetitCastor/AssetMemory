using AssetMemory.Core.Resolution;

namespace AssetMemory.Core.Tests.Resolution;

public class SystemNameResolverTests
{
    [Theory]
    [InlineData("Stanton4_NewBabbage", "Stanton")]
    [InlineData("Stanton1_Lorville", "Stanton")]
    [InlineData("Stanton3_Area18", "Stanton")]
    [InlineData("Stanton2_Orison", "Stanton")]
    [InlineData("Stanton3a_Shubin_SAL2", "Stanton")]
    [InlineData("Stanton4_DistributionCentre_Covalex_S4DC05", "Stanton")]
    [InlineData("Stanton1_HurdynMining_HDMSStanhope", "Stanton")]
    [InlineData("RR_HUR_LEO", "Stanton")]
    [InlineData("RR_MIC_LEO", "Stanton")]
    [InlineData("RR_ARC_LEO", "Stanton")]
    [InlineData("RR_CRU_LEO", "Stanton")]
    [InlineData("RR_HUR_L2", "Stanton")]
    [InlineData("RR_ARC_L1", "Stanton")]
    [InlineData("RR_CRU_L4", "Stanton")]
    public void Resolve_buckets_stanton_codes(string code, string expected)
        => Assert.Equal(expected, new SystemNameResolver().Resolve(code));

    [Theory]
    [InlineData("Nyx_Levski", "Nyx")]
    [InlineData("Nyx_Kaboos", "Nyx")]
    [InlineData("Nyx_TSG_QVExtractionStation_033", "Nyx")]
    public void Resolve_buckets_nyx_codes(string code, string expected)
        => Assert.Equal(expected, new SystemNameResolver().Resolve(code));

    [Theory]
    [InlineData("RR_P2_L4", "Pyro")]
    public void Resolve_buckets_pyro_codes(string code, string expected)
        => Assert.Equal(expected, new SystemNameResolver().Resolve(code));

    [Theory]
    [InlineData("RR_JP_NyxCastra", "Nyx")]      // real log code: physically in Nyx, gateway to Stanton
    [InlineData("RR_JP_NyxPyro", "Nyx")]
    [InlineData("RR_JP_StantonPyro", "Stanton")]
    public void Resolve_buckets_a_jump_point_under_its_origin_system(string code, string expected)
        => Assert.Equal(expected, new SystemNameResolver().Resolve(code));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SomeUnrecognizedCode_Whatever")]
    [InlineData("RR_XYZ_L1")]
    [InlineData("RR_JP_Whatever")]
    [InlineData("RR_JP")]
    public void Resolve_falls_back_to_other_for_unrecognized_or_empty_codes(string? code)
        => Assert.Equal("Other", new SystemNameResolver().Resolve(code));
}
