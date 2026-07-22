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
    // The system token is not always the leading one — outposts, prisons and asteroid bases bury it.
    [InlineData("Outpost_OLP_Stanton2b_Lamina", "Stanton")]
    [InlineData("Outpost_PAF_Stanton1b_Ruptura_3", "Stanton")]
    [InlineData("PrisonMine_Stanton1b", "Stanton")]
    public void Resolve_buckets_stanton_codes(string code, string expected)
        => Assert.Equal(expected, new SystemNameResolver().Resolve(code));

    [Theory]
    [InlineData("Nyx_Levski", "Nyx")]
    [InlineData("Nyx_Kaboos", "Nyx")]
    [InlineData("Nyx_TSG_QVExtractionStation_033", "Nyx")]
    // Real log codes where "Nyx" is the second token, not the first.
    [InlineData("AsteroidClusterBase_Nyx_Social_Keeger_002", "Nyx")]
    [InlineData("AsteroidClusterBase_Nyx_Social_Keeger_004", "Nyx")]
    public void Resolve_buckets_nyx_codes(string code, string expected)
        => Assert.Equal(expected, new SystemNameResolver().Resolve(code));

    [Theory]
    [InlineData("RR_P2_L4", "Pyro")]
    [InlineData("RR_P5_L2", "Pyro")]
    [InlineData("RR_P6_LEO", "Pyro")]
    // Pyro planet tokens (no rest-stop RR_ prefix) — previously unrecognized, fell to "Other".
    [InlineData("Pyro4_Outpost_col_m_trdpst_indy_001", "Pyro")]
    [InlineData("Pyro1_ASD_Monorail_LazarusTransportHub_Tithonus_2A", "Pyro")]
    [InlineData("Pyro5c_Outpost_col_m_hmstd_indy_001", "Pyro")]
    public void Resolve_buckets_pyro_codes(string code, string expected)
        => Assert.Equal(expected, new SystemNameResolver().Resolve(code));

    [Theory]
    [InlineData("RR_JP_NyxCastra", "Nyx")]      // real log code: physically in Nyx, gateway to Stanton
    [InlineData("RR_JP_NyxPyro", "Nyx")]
    [InlineData("RR_JP_StantonPyro", "Stanton")]
    [InlineData("RR_JP_StantonMagnus", "Stanton")]
    [InlineData("RR_JP_PyroNyx", "Pyro")]       // origin (leading token) wins over the destination
    [InlineData("RR_JP_PyroStanton", "Pyro")]
    public void Resolve_buckets_a_jump_point_under_its_origin_system(string code, string expected)
        => Assert.Equal(expected, new SystemNameResolver().Resolve(code));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SomeUnrecognizedCode_Whatever")]
    [InlineData("RR_XYZ_L1")]
    [InlineData("RR_JP_Whatever")]
    [InlineData("RR_JP")]
    [InlineData("INVALID_LOCATION_ID")]   // the game's sentinel for "no real place"
    public void Resolve_falls_back_to_other_for_unrecognized_or_empty_codes(string? code)
        => Assert.Equal("Other", new SystemNameResolver().Resolve(code));
}
