using System.Text.RegularExpressions;

namespace AssetMemory.Core.Resolution;

/// <summary>
/// Buckets a log station code by starmap system. Jump points (<c>RR_JP_&lt;Origin&gt;&lt;Destination&gt;</c>,
/// e.g. <c>RR_JP_NyxCastra</c>) are wormhole gateways that sit physically in one system and are named
/// for where they lead — the origin is always the leading camelCase token, so a JP buckets under its
/// origin system just like any other place there (<c>RR_JP_NyxCastra</c>, <c>RR_JP_NyxPyro</c> ->
/// <c>"Nyx"</c>; the destination token is decorative and never itself consulted). Codes this can't
/// place — unrecognized prefixes, and freestanding local-storage containers that never went through a
/// station-identified event — fall to <c>"Other"</c>.
/// </summary>
public sealed partial class SystemNameResolver : ISystemNameResolver
{
    private static readonly HashSet<string> StantonBodies = new(StringComparer.Ordinal)
    {
        "HUR", "MIC", "ARC", "CRU",
    };

    [GeneratedRegex(@"^Stanton\d[a-c]?$", RegexOptions.Compiled)]
    private static partial Regex StantonSystemTag();

    [GeneratedRegex(@"^P\d+$", RegexOptions.Compiled)]
    private static partial Regex PyroBody();

    public string Resolve(string? stationCode)
    {
        if (string.IsNullOrWhiteSpace(stationCode))
            return "Other";

        var parts = stationCode.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return "Other";

        // Rest stops: RR_<body-or-JP>_<orbit…> — the system tag isn't the leading token.
        if (parts[0] == "RR" && parts.Length >= 2)
        {
            if (parts[1] == "JP")
                return parts.Length >= 3 ? JumpPointOrigin(parts[2]) : "Other";
            if (StantonBodies.Contains(parts[1]))
                return "Stanton";
            if (PyroBody().IsMatch(parts[1]))
                return "Pyro";
            return "Other";
        }

        if (StantonSystemTag().IsMatch(parts[0]))
            return "Stanton";

        if (parts[0] == "Nyx")
            return "Nyx";

        return "Other";
    }

    /// <summary>The leading camelCase token of a JP's destination-named tail is the system it
    /// physically sits in (e.g. <c>NyxCastra</c> -> <c>Nyx</c>).</summary>
    private static string JumpPointOrigin(string tail)
    {
        if (tail.StartsWith("Stanton", StringComparison.Ordinal))
            return "Stanton";
        if (tail.StartsWith("Nyx", StringComparison.Ordinal))
            return "Nyx";
        if (tail.StartsWith("Pyro", StringComparison.Ordinal))
            return "Pyro";
        return "Other";
    }
}
