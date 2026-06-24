namespace AssetMemory.Core.Resolution;

/// <summary>
/// Resolves item display names by consulting a curated override map first, then
/// falling back to a <see cref="IItemNameFormatter"/> (heuristic by default).
/// </summary>
public sealed class ItemNameResolver : IItemNameResolver
{
    private readonly IReadOnlyDictionary<string, string> _overrides;
    private readonly IItemNameFormatter _fallback;

    public ItemNameResolver(
        IReadOnlyDictionary<string, string>? overrides = null,
        IItemNameFormatter? fallback = null)
    {
        _overrides = overrides ?? new Dictionary<string, string>(StringComparer.Ordinal);
        _fallback = fallback ?? new HeuristicItemNameFormatter();
    }

    public string Resolve(string? itemClass)
    {
        if (string.IsNullOrEmpty(itemClass))
            return _fallback.Format(itemClass);

        return _overrides.TryGetValue(itemClass, out var name)
            ? name
            : _fallback.Format(itemClass);
    }

    public bool HasOverride(string itemClass) => _overrides.ContainsKey(itemClass);
}
