using AssetMemory.Core.Inventory.Events;
using AssetMemory.Core.Logs;

namespace AssetMemory.Core.Inventory;

/// <summary>
/// Streams raw <c>Game.log</c> lines into typed <see cref="InventoryEvent"/>s, silently
/// skipping any line that is not a recognised inventory event.
/// </summary>
public sealed class InventoryLogReader
{
    private readonly InventoryEventParser _parser;

    public InventoryLogReader(InventoryEventParser? parser = null)
        => _parser = parser ?? new InventoryEventParser();

    public IEnumerable<InventoryEvent> Read(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        foreach (var line in lines)
        {
            if (LogEntryParser.TryParse(line, out var entry)
                && _parser.TryParse(entry, out var ev))
            {
                yield return ev;
            }
        }
    }
}
