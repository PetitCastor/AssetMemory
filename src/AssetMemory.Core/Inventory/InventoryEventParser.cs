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
        // Must precede ContainerOpenedParser: it stashes the box class off the OpenNestedInventory
        // line (returning false so ContainerOpenedParser still emits its open event) and identifies
        // the box on the paired numeric-ref query.
        new NestedContainerParser(),
        new ContainerOpenedParser(),
        new MoveEventParser(),
        // Must follow MoveEventParser: it claims a bulk "Move all" whose items live only in the batched
        // "Add Inventory Management Move … ItemClass[[c1] [c2] …]" line (paired Queued line is NULL), which
        // MoveEventParser's per-item gate skips. On an ordinary per-item Queued line MoveEventParser runs
        // first and wins, so this never double-counts.
        new BatchMoveEventParser(),
        new StoreEventParser(),
        new DropEventParser(),
        new GridItemCountParser(),
        new EquippedItemParser(),
        new EquipFromInventoryParser(),
        new ContainerClosedParser(),
        new StationInventoryParser(),
        new PlayerLocationParser(),
        new FreightInventoryParser(),
        new FreightDescendedParser(),
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
