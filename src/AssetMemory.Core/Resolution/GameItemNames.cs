namespace AssetMemory.Core.Resolution;

/// <summary>
/// Builds a class-name → display-name map from Star Citizen's localization file
/// (<c>&lt;gameDir&gt;/Data/Localization/english/global.ini</c>), which ships loose with the
/// install. Entries look like <c>item_Namebehr_smg_ballistic_01=P8-SC SMG</c>. This is static
/// game data (unlike ephemeral entity IDs), so it is a reliable local source of real names.
/// </summary>
public static class GameItemNames
{
    private const string NamePrefix = "item_Name";

    /// <summary>Resolves the loose localization file next to a configured <c>Game.log</c>, or null if absent.</summary>
    public static string? ResolveIniPath(string? gameLogPath)
    {
        if (string.IsNullOrEmpty(gameLogPath))
            return null;

        var gameDir = Path.GetDirectoryName(gameLogPath);
        if (string.IsNullOrEmpty(gameDir))
            return null;

        var ini = Path.Combine(gameDir, "Data", "Localization", "english", "global.ini");
        return File.Exists(ini) ? ini : null;
    }

    /// <summary>Loads the item-name map for the install owning <paramref name="gameLogPath"/>; empty if unavailable.</summary>
    public static IReadOnlyDictionary<string, string> LoadForGameLog(string? gameLogPath)
    {
        var ini = ResolveIniPath(gameLogPath);
        if (ini is null)
            return EmptyMap();

        try
        {
            return Parse(File.ReadLines(ini));
        }
        catch (IOException)
        {
            return EmptyMap();
        }
    }

    /// <summary>
    /// Parses <c>item_Name&lt;class&gt;=&lt;name&gt;</c> lines into a case-insensitive map. Skips the
    /// <c>_short</c> UI variants, non-name keys, and empty values.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            if (!line.StartsWith(NamePrefix, StringComparison.Ordinal))
                continue;

            var eq = line.IndexOf('=');
            if (eq < 0)
                continue;

            var key = line[..eq];
            if (key.EndsWith("_short", StringComparison.Ordinal))
                continue;

            // Weapons key as item_Name<class>; armor/clothing as item_Name_<class>. The log emits
            // the class with no leading underscore, so normalise it away to match either form.
            var className = key[NamePrefix.Length..].TrimStart('_');
            var name = line[(eq + 1)..].TrimEnd('\r', '\n', ' ');
            if (className.Length == 0 || name.Length == 0)
                continue;

            map[className] = name; // last wins on any case-only collision
        }

        return map;
    }

    private static Dictionary<string, string> EmptyMap() => new(StringComparer.OrdinalIgnoreCase);
}
