using AssetMemory.Core.Inventory;
using AssetMemory.Core.Inventory.Events;
using AssetMemory.Core.Resolution;

namespace AssetMemory.Data.Events;

/// <summary>
/// Applies parsed <see cref="InventoryEvent"/>s to an <see cref="AssetMemoryStore"/>:
/// ledger-style updates to holdings on moves, equipped-loadout updates, location bookkeeping
/// on open/close, and station naming. Every event is also recorded to the audit table.
/// </summary>
public sealed class EventApplier
{
    private readonly AssetMemoryStore _store;
    private readonly IItemNameResolver _names;
    private readonly IStationNameResolver _stations;

    public EventApplier(AssetMemoryStore store, IItemNameResolver names, IStationNameResolver? stations = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _names = names ?? throw new ArgumentNullException(nameof(names));
        _stations = stations ?? new StationNameResolver();
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
            case StationIdentifiedEvent station: ApplyStation(station); break;
            case GridItemCountEvent: /* no-op — purely a UI hint, no identity */ break;
        }

        _store.RecordAudit(ev.Timestamp, ev.GetType().Name, ev.ToString() ?? "");
    }

    /// <summary>
    /// The holding key for an inventory ref. A station's local inventory is <c>GEID:Location:placeId</c>
    /// where the place identity is the <see cref="InventoryRef.Id"/> (the same GEID owns every station
    /// the player visits); containers carry their identity in the <see cref="InventoryRef.Owner"/>.
    /// </summary>
    private static long LocationKey(InventoryRef r)
        => r.Kind == InventoryKind.Location ? r.Id : r.Owner;

    private void ApplyMove(ItemMovedEvent move)
    {
        var itemId = _store.EnsureItem(move.ItemClass, _names.Resolve(move.ItemClass));

        var source = LocationKey(move.Source);
        var target = LocationKey(move.Target);

        _store.UpsertLocation(source, move.Timestamp, label: null);
        _store.UpsertLocation(target, move.Timestamp, label: null);

        _store.AdjustHolding(source, itemId, -move.Quantity, move.Timestamp);
        _store.AdjustHolding(target, itemId, +move.Quantity, move.Timestamp);
    }

    private void ApplyEquipped(EquippedItemEvent eq)
    {
        var itemId = _store.EnsureItem(eq.ItemClass, _names.Resolve(eq.ItemClass));
        _store.UpsertEquipped(
            eq.Player, eq.Port, itemId, eq.EntityId,
            eq.InstanceName, eq.Status, eq.Timestamp);
    }

    private void ApplyOpened(ContainerOpenedEvent open)
        => _store.UpsertLocation(open.Container.Id, open.Timestamp, label: null);

    private void ApplyClosed(ContainerClosedEvent close)
        => _store.UpsertLocation(close.Container.Id, close.Timestamp, label: null);

    private void ApplyStation(StationIdentifiedEvent station)
    {
        var label = _stations.Resolve(station.StationCode);
        _store.UpsertLocation(station.PlaceId, station.Timestamp,
            string.IsNullOrEmpty(label) ? null : label);
    }
}
