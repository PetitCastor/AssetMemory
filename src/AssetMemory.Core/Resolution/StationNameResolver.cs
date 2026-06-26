namespace AssetMemory.Core.Resolution;

/// <summary>
/// Resolves station codes to display names: a curated override map of well-known hubs first,
/// then a <see cref="IStationNameFormatter"/> (heuristic by default) for the long tail of
/// mining outposts, distribution centres and rest stops.
/// </summary>
public sealed class StationNameResolver : IStationNameResolver
{
    /// <summary>High-confidence names for the busiest hubs — landing zones and R&amp;R LEO stations.</summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultOverrides =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Stanton1_Lorville"] = "Lorville",
            ["Stanton2_Orison"] = "Orison",
            ["Stanton3_Area18"] = "Area18",
            ["Stanton4_NewBabbage"] = "New Babbage",
            ["Nyx_Levski"] = "Levski",
            ["Nyx_Kaboos"] = "Kaboos",
            ["RR_HUR_LEO"] = "Everus Harbor",
            ["RR_MIC_LEO"] = "Port Tressler",
            ["RR_ARC_LEO"] = "Baijini Point",
            ["RR_CRU_LEO"] = "Seraphim Station",
        };

    private readonly IReadOnlyDictionary<string, string> _overrides;
    private readonly IStationNameFormatter _fallback;

    public StationNameResolver(
        IReadOnlyDictionary<string, string>? overrides = null,
        IStationNameFormatter? fallback = null)
    {
        _overrides = overrides ?? DefaultOverrides;
        _fallback = fallback ?? new HeuristicStationNameFormatter();
    }

    public string Resolve(string? stationCode)
    {
        if (string.IsNullOrEmpty(stationCode))
            return "";

        return _overrides.TryGetValue(stationCode, out var name)
            ? name
            : _fallback.Format(stationCode);
    }
}
