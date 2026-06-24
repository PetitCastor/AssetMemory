using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AssetMemory.Core.Logs;

/// <summary>
/// Parses the common envelope of a <c>Game.log</c> line:
/// <c>&lt;timestamp&gt; [Severity] &lt;Category&gt; message…</c>
/// </summary>
public static partial class LogEntryParser
{
    // <2026-06-24T19:22:39.827Z> [Notice] <InventoryManagement> rest of the message...
    // Category may contain spaces ("Remove Inventory Container UI"); message is everything after it.
    [GeneratedRegex(
        @"^<(?<ts>[^>]+)>\s+\[(?<sev>[^\]]+)\]\s+<(?<cat>[^>]+)>\s*(?<msg>.*)$",
        RegexOptions.Compiled)]
    private static partial Regex EnvelopeRegex();

    public static bool TryParse(string? line, [NotNullWhen(true)] out LogEntry? entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var raw = line;
        var trimmed = line.TrimEnd('\r', '\n');

        var match = EnvelopeRegex().Match(trimmed);
        if (!match.Success)
            return false;

        if (!DateTimeOffset.TryParse(
                match.Groups["ts"].Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp))
            return false;

        entry = new LogEntry(
            timestamp,
            match.Groups["sev"].Value,
            match.Groups["cat"].Value,
            match.Groups["msg"].Value,
            raw);
        return true;
    }
}
