using System.Globalization;
using System.Text.Json;

namespace AssetMemory.Core.Resolution;

/// <summary>
/// Reads and writes the portable JSON files that back <see cref="ItemNameResolver"/>
/// and <see cref="LocationLabelResolver"/>. Saves are sorted and indented so the files
/// stay diff-friendly and editable by hand.
/// </summary>
public static class ResolverJsonLoader
{
    private static readonly JsonWriterOptions IndentedWriter = new() { Indented = true };

    // ----- item overrides: { "item_class": "Display Name" } -----

    public static IReadOnlyDictionary<string, string> LoadItemOverrides(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Expected a JSON object at the root.");

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException(
                        $"Override for '{property.Name}' must be a string.");
                result[property.Name] = property.Value.GetString()!;
            }
            return result;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Malformed item-override JSON.", ex);
        }
    }

    public static void SaveItemOverrides(
        IReadOnlyDictionary<string, string> overrides,
        Stream stream)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        ArgumentNullException.ThrowIfNull(stream);

        using var writer = new Utf8JsonWriter(stream, IndentedWriter);
        writer.WriteStartObject();
        foreach (var key in overrides.Keys.OrderBy(k => k, StringComparer.Ordinal))
            writer.WriteString(key, overrides[key]);
        writer.WriteEndObject();
    }

    // ----- location labels: { "2900774186": "Aaron Halo Outpost" } -----

    public static IReadOnlyDictionary<long, string> LoadLocationLabels(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Expected a JSON object at the root.");

            var result = new Dictionary<long, string>();
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (!long.TryParse(
                        property.Name,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var id))
                {
                    throw new InvalidDataException(
                        $"Location key '{property.Name}' is not a 64-bit integer.");
                }
                if (property.Value.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException(
                        $"Label for location {id} must be a string.");
                result[id] = property.Value.GetString()!;
            }
            return result;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Malformed location-labels JSON.", ex);
        }
    }

    public static void SaveLocationLabels(
        IReadOnlyDictionary<long, string> labels,
        Stream stream)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(stream);

        using var writer = new Utf8JsonWriter(stream, IndentedWriter);
        writer.WriteStartObject();
        foreach (var id in labels.Keys.OrderBy(k => k))
            writer.WriteString(id.ToString(CultureInfo.InvariantCulture), labels[id]);
        writer.WriteEndObject();
    }
}
