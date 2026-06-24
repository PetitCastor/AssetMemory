using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace AssetMemory.Core.Resolution;

/// <summary>
/// Maps location entity ids (from <c>Game.log</c>) to user-curated labels. Unknown ids
/// can be returned as a synthetic <c>"Location {id}"</c> string so the UI never has to
/// fall back to blank text.
/// </summary>
public sealed class LocationLabelResolver
{
    private readonly Dictionary<long, string> _labels;

    public LocationLabelResolver()
        : this(null)
    {
    }

    public LocationLabelResolver(IReadOnlyDictionary<long, string>? seed)
    {
        _labels = seed is null
            ? new Dictionary<long, string>()
            : new Dictionary<long, string>(seed);
    }

    public IEnumerable<long> KnownIds => _labels.Keys;

    public bool TryResolve(long id, [NotNullWhen(true)] out string? label)
        => _labels.TryGetValue(id, out label);

    public string ResolveOrFallback(long id)
        => _labels.TryGetValue(id, out var label)
            ? label
            : $"Location {id.ToString(CultureInfo.InvariantCulture)}";

    public void Set(long id, string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("Label cannot be null or whitespace.", nameof(label));
        _labels[id] = label;
    }

    public bool Remove(long id) => _labels.Remove(id);

    public IReadOnlyDictionary<long, string> Snapshot()
        => new Dictionary<long, string>(_labels);
}
