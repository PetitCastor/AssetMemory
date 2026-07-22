using System.Text.RegularExpressions;

namespace AssetMemory.Core.Resolution;

/// <summary>
/// Buckets a log station code by starmap system. The system tag can sit anywhere in the code, not
/// just the leading token: landing zones lead with it (<c>Nyx_Levski</c>, <c>Stanton1_Lorville</c>),
/// but asteroid bases and outposts bury it (<c>AsteroidClusterBase_Nyx_Social_Keeger_002</c> -> Nyx,
/// <c>Outpost_OLP_Stanton2b_Lamina</c> / <c>PrisonMine_Stanton1b</c> -> Stanton), so every underscore
/// token is scanned. Planet tokens count (<c>Stanton4</c>, <c>Pyro4</c>, <c>Stanton1b</c>). R&amp;R
/// rest stops carry a body code (<c>RR_HUR_LEO</c> -> Stanton, <c>RR_P2_L4</c> -> Pyro). Jump points
/// (<c>RR_JP_&lt;Origin&gt;&lt;Destination&gt;</c>) are named for where they lead but sit in their
/// origin — the leading camelCase token — so a prefix match on the concatenated token buckets them
/// correctly (<c>NyxCastra</c> -> Nyx, <c>PyroNyx</c> -> Pyro; the trailing destination never wins).
/// Only codes with no recognizable system token — chiefly <c>INVALID_LOCATION_ID</c> and freestanding
/// containers never seen at a station — fall to <c>"Other"</c>.
/// </summary>
public sealed partial class SystemNameResolver : ISystemNameResolver
{
    private static readonly HashSet<string> StantonBodies = new(StringComparer.Ordinal)
    {
        "HUR", "MIC", "ARC", "CRU",
    };

    // A rest-stop / jump-point Pyro body code: P2, P5, P6… (distinct from a Pyro<n> planet token).
    [GeneratedRegex(@"^P\d+$", RegexOptions.Compiled)]
    private static partial Regex PyroBody();

    public string Resolve(string? stationCode)
    {
        if (string.IsNullOrWhiteSpace(stationCode))
            return "Other";

        // Scan every underscore token; the first that names a system wins. Non-JP codes carry exactly
        // one system token, and a JP's origin is the concatenated tail token, so first-match is safe.
        foreach (var token in stationCode.Split('_', StringSplitOptions.RemoveEmptyEntries))
        {
            var system = SystemForToken(token);
            if (system is not null)
                return system;
        }

        return "Other";
    }

    /// <summary>The system a single code token names, or null if it names none. Prefix matches on the
    /// system name cover planet tokens (<c>Stanton4a</c>), bare systems (<c>Nyx</c>), and jump-point
    /// origins (<c>PyroNyx</c> -> Pyro, checked before Nyx so the origin, not the destination, wins).</summary>
    private static string? SystemForToken(string token)
    {
        if (token.StartsWith("Stanton", StringComparison.Ordinal))
            return "Stanton";
        if (token.StartsWith("Pyro", StringComparison.Ordinal))
            return "Pyro";
        if (token.StartsWith("Nyx", StringComparison.Ordinal))
            return "Nyx";
        if (StantonBodies.Contains(token))
            return "Stanton";
        if (PyroBody().IsMatch(token))
            return "Pyro";
        return null;
    }
}
