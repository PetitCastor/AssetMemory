using System.Diagnostics.CodeAnalysis;
using AssetMemory.Core.Inventory.Events;
using AssetMemory.Core.Logs;

namespace AssetMemory.Core.Inventory;

/// <summary>
/// Dispatches a <see cref="LogEntry"/> to the first <see cref="IInventoryEventParser"/> that
/// recognises it. Defaults to the full set of known inventory parsers.
/// </summary>
public sealed class InventoryEventParser
{
    private readonly IReadOnlyList<IInventoryEventParser> _parsers;

    public InventoryEventParser()
        : this(DefaultParsers())
    {
    }

    public InventoryEventParser(IReadOnlyList<IInventoryEventParser> parsers)
        => _parsers = parsers;

    public static IReadOnlyList<IInventoryEventParser> DefaultParsers() =>
    [
        new ContainerOpenedParser(),
        new MoveEventParser(),
        new GridItemCountParser(),
        new EquippedItemParser(),
        new ContainerClosedParser(),
        new PlayerIdentityParser(),
    ];

    public bool TryParse(LogEntry entry, [NotNullWhen(true)] out InventoryEvent? ev)
    {
        foreach (var parser in _parsers)
        {
            if (parser.TryParse(entry, out ev))
                return true;
        }

        ev = null;
        return false;
    }
}
