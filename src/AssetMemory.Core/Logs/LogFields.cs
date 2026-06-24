using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace AssetMemory.Core.Logs;

/// <summary>
/// Extracts <c>Key[value]</c> fields out of a log message body. A key only matches when
/// it is not the suffix of a larger word (so <c>"Inventory"</c> will not match inside
/// <c>"SourceInventory"</c>). The first occurrence wins.
/// </summary>
public static class LogFields
{
    public static string? Get(string text, string key)
        => TryGet(text, key, out var value) ? value : null;

    public static bool TryGet(string text, string key, [NotNullWhen(true)] out string? value)
    {
        value = null;
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(key))
            return false;

        // (?<![A-Za-z0-9_]) ensures the key starts on a word boundary; value is up to the next ']'.
        var pattern = $@"(?<![A-Za-z0-9_]){Regex.Escape(key)}\[([^\]]*)\]";
        var match = Regex.Match(text, pattern);
        if (!match.Success)
            return false;

        value = match.Groups[1].Value;
        return true;
    }
}
