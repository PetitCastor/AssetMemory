using System.Text;
using System.Text.RegularExpressions;

namespace AssetMemory.Core.Resolution;

/// <summary>
/// Deterministic fallback for station codes with no curated name. Drops a redundant leading
/// <c>StantonN[a-c]</c> system tag, expands R&amp;R rest-stop body codes (e.g. <c>HUR</c> →
/// Hurston), and splits the rest on underscores and camelCase boundaries so codes like
/// <c>Stanton4_DistributionCentre_Covalex_S4DC05</c> read as "Distribution Centre Covalex S4DC05".
/// </summary>
public sealed partial class HeuristicStationNameFormatter : IStationNameFormatter
{
    private static readonly Dictionary<string, string> Bodies = new(StringComparer.Ordinal)
    {
        ["HUR"] = "Hurston",
        ["MIC"] = "microTech",
        ["ARC"] = "ArcCorp",
        ["CRU"] = "Crusader",
    };

    [GeneratedRegex(@"^Stanton\d[a-c]?$", RegexOptions.Compiled)]
    private static partial Regex StantonSystemTag();

    [GeneratedRegex(@"^P(\d+)$", RegexOptions.Compiled)]
    private static partial Regex PyroBody();

    public string Format(string? stationCode)
    {
        if (string.IsNullOrWhiteSpace(stationCode))
            return "";

        var parts = stationCode.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return "";

        // Rest stops: RR_<body>_<orbit…>  e.g. RR_HUR_L2, RR_P2_L4, RR_JP_NyxPyro
        if (parts.Length >= 2 && parts[0] == "RR")
        {
            if (parts[1] == "JP")
                return $"{SplitWords(string.Join(' ', parts[2..]))} Jump Point".Trim();

            var body = ExpandBody(parts[1]);
            var orbit = string.Join(' ', parts[2..]);
            return string.Join(' ', new[] { body, orbit }.Where(s => s.Length > 0));
        }

        var start = StantonSystemTag().IsMatch(parts[0]) ? 1 : 0;
        return string.Join(' ', parts[start..].Select(SplitWords));
    }

    private static string ExpandBody(string code)
    {
        if (Bodies.TryGetValue(code, out var name))
            return name;
        var pyro = PyroBody().Match(code);
        return pyro.Success ? $"Pyro {pyro.Groups[1].Value}" : code;
    }

    /// <summary>Inserts spaces at camelCase boundaries (acronym-aware): "HDMSStanhope" → "HDMS Stanhope".</summary>
    private static string SplitWords(string token)
    {
        if (token.Length == 0)
            return token;

        var sb = new StringBuilder(token.Length + 4);
        for (var i = 0; i < token.Length; i++)
        {
            var c = token[i];
            if (i > 0 && char.IsUpper(c))
            {
                var prev = token[i - 1];
                var nextIsLower = i + 1 < token.Length && char.IsLower(token[i + 1]);
                if (char.IsLower(prev) || (char.IsUpper(prev) && nextIsLower))
                    sb.Append(' ');
            }
            sb.Append(c);
        }
        return sb.ToString();
    }
}
