using AssetMemory.Core.Inventory.Events;
using AssetMemory.Core.Resolution;

namespace AssetMemory.Data.Events;

/// <summary>
/// Applies parsed <see cref="InventoryEvent"/>s to an <see cref="AssetMemoryStore"/>:
/// ledger-style updates to holdings on moves, equipped-loadout updates, location bookkeeping
/// on open/close. Every event is also recorded to the audit table for traceability.
/// </summary>
public sealed class EventApplier
{
    private readonly AssetMemoryStore _store;
    private readonly IItemNameResolver _names;

    public EventApplier(AssetMemoryStore store, IItemNameResolver names)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _names = names ?? throw new ArgumentNullException(nameof(names));
    }

    public void Apply(InventoryEvent ev)
    {
        ArgumentNullException.ThrowIfNull(ev);

        switch (ev)
        {
            case ItemMovedEvent move: ApplyMove(move); break;
            case EquippedItemEvent eq: ApplyEquipped(eq); break;
            case ContainerOpenedEvent open: ApplyOpened(open); break;
            case ContainerClosedEvent close: ApplyClosed(close); break;
            case PlayerIdentityEvent id: ApplyPlayerIdentity(id); break;
            case GridItemCountEvent: /* no-op — purely a UI hint, no identity */ break;
        }

        _store.RecordAudit(ev.Timestamp, ev.GetType().Name, ev.ToString() ?? "");
    }

    private void ApplyMove(ItemMovedEvent move)
    {
        var itemId = _store.EnsureItem(move.ItemClass, _names.Resolve(move.ItemClass));

        _store.UpsertLocation(move.Source.Owner, move.Timestamp, label: null);
        _store.UpsertLocation(move.Target.Owner, move.Timestamp, label: null);

        _store.AdjustHolding(move.Source.Owner, itemId, -move.Quantity, move.Timestamp);
        _store.AdjustHolding(move.Target.Owner, itemId, +move.Quantity, move.Timestamp);
    }

    private void ApplyEquipped(EquippedItemEvent eq)
    {
        var displayName = _names.Resolve(eq.ItemClass);
        var itemId = _store.EnsureItem(eq.ItemClass, displayName);
        _store.UpsertEquipped(
            eq.Player, eq.Port, itemId, eq.EntityId,
            eq.InstanceName, eq.Status, eq.Timestamp);

        // The worn item is itself a container (e.g. armor with pockets) — its entity id
        // is the same id moves use as the owning location, so label it from the item name.
        _store.UpsertLocation(eq.EntityId, eq.Timestamp, label: $"{displayName} (worn)");
    }

    private void ApplyOpened(ContainerOpenedEvent open)
        => _store.UpsertLocation(open.Container.Id, open.Timestamp, label: null);

    private void ApplyClosed(ContainerClosedEvent close)
        => _store.UpsertLocation(close.Container.Id, close.Timestamp, label: null);

    private void ApplyPlayerIdentity(PlayerIdentityEvent id)
    {
        if (string.IsNullOrEmpty(id.Player))
            return;

        // A player's GEID doubles as the location id for their own personal inventory.
        _store.UpsertLocation(id.Geid, id.Timestamp, label: id.Player);
    }
}
