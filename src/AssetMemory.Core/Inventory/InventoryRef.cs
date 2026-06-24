using System.Diagnostics.CodeAnalysis;

namespace AssetMemory.Core.Inventory;

public enum InventoryKind
{
    Unknown = 0,
    Location,
    Container,
    ClientOnly,
}

/// <summary>
/// A reference to a Star Citizen inventory, formatted in the log as
/// <c>owner:Kind:id</c> (e.g. <c>204821708183:Location:2900774186</c>).
/// </summary>
public readonly record struct InventoryRef(long Owner, InventoryKind Kind, long Id, string Raw)
{
    public static bool TryParse([NotNullWhen(true)] string? text, out InventoryRef value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Split(':');
        if (parts.Length != 3)
            return false;

        if (!long.TryParse(parts[0], out var owner))
            return false;
        if (!long.TryParse(parts[2], out var id))
            return false;

        var kind = parts[1] switch
        {
            "Location" => InventoryKind.Location,
            "Container" => InventoryKind.Container,
            "ClientOnly" => InventoryKind.ClientOnly,
            _ => InventoryKind.Unknown,
        };

        value = new InventoryRef(owner, kind, id, text);
        return true;
    }
}
