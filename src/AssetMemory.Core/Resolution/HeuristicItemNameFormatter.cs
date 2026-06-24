namespace AssetMemory.Core.Resolution;

/// <summary>
/// Deterministic fallback formatter: splits the class name on underscores, drops empty
/// segments, and capitalizes only the first letter of the first segment. Internal casing
/// is preserved so manufacturer/variant tokens like <c>TBO</c> or <c>2SCU</c> stay legible.
/// </summary>
public sealed class HeuristicItemNameFormatter : IItemNameFormatter
{
    public string Format(string? itemClass)
    {
        if (string.IsNullOrWhiteSpace(itemClass))
            return "";

        var segments = itemClass.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return "";

        var first = segments[0];
        if (first.Length > 0 && char.IsLower(first[0]))
            segments[0] = char.ToUpperInvariant(first[0]) + first[1..];

        return string.Join(' ', segments);
    }
}
