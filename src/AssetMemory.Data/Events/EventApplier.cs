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
    private readonly ISystemNameResolver _systems;

    public EventApplier(
        AssetMemoryStore store, IItemNameResolver names,
        IStationNameResolver? stations = null, ISystemNameResolver? systems = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _names = names ?? throw new ArgumentNullException(nameof(names));
        _stations = stations ?? new StationNameResolver();
        _systems = systems ?? new SystemNameResolver();
    }

    /// <summary>
    /// Optional lower bound: events dated before this instant are skipped entirely (not applied, not
    /// audited, not counted). Null ingests everything. Mutable so the sync-inception picker can retune
    /// it on the shared singleton at runtime; the next rebuild replays the log through the new bound.
    /// </summary>
    public DateTimeOffset? InceptionUtc { get; set; }

    /// <summary>
    /// The place id of the most recently applied <see cref="StationIdentifiedEvent"/> -- the
    /// player's last known location, used to nest a drop's synthetic entity location under wherever
    /// it actually happened instead of leaving it a disconnected top-level row. Transient, in-memory
    /// session state (not persisted): a full rebuild replays <see cref="StationIdentifiedEvent"/>s in
    /// log order too, so it self-corrects during replay -- but the caller must still call
    /// <see cref="ResetSessionState"/> alongside wiping the store, or a rebuild's earliest drops (before
    /// the replay reaches its first station) would wrongly inherit the place from the previous run.
    /// </summary>
    private long? _lastKnownPlaceId;

    /// <summary>
    /// The system bucket (see <see cref="ISystemNameResolver"/>) of the player's last known location.
    /// Companion to <see cref="_lastKnownPlaceId"/> for the case where the location is known only by
    /// system, not by a numeric place -- e.g. a hangar loading platform tagged <c>Nyx</c>, or a
    /// <see cref="StationIdentifiedEvent"/> whose place row exists but whose drop falls through the
    /// top-level fallback below. Lets such a drop surface under its real system instead of "Other".
    /// Same transient, self-correcting-on-replay contract as <see cref="_lastKnownPlaceId"/>.
    /// </summary>
    private string? _lastKnownSystem;

    // The system resolvers' sentinel for "couldn't place it" (see SystemNameResolver). Treated as
    // "no information" here: it never overwrites a real system we already hold.
    private const string UnknownSystem = "Other";

    // Reserved synthetic location for items dropped where no freight descent tied them to a station
    // (e.g. a mission site). One flagged bucket with its own top-level system so it never falls into
    // "Other"; items sit here, retained, until something is done with them. A negative id cannot
    // collide with a real GEID / place / entity id (all positive).
    private const long DroppedBucketId = -1;
    private const string DroppedLabel = "Dropped";
    private const string DroppedSystem = "Dropped";

    // How long after a drop a freight descent may still claim it as a station deposit. The descent
    // logs ~10-15s after the drop in practice; 60s is comfortable slack.
    private static readonly TimeSpan FreightMergeWindow = TimeSpan.FromSeconds(60);

    // Drops awaiting a possible freight descent: each is credited to the Dropped bucket on arrival,
    // then moved onto the current station's inventory if a descent follows within FreightMergeWindow.
    private readonly List<(DateTimeOffset At, long ItemId, int Qty)> _pendingFreight = [];

    /// <summary>Clears transient session state. Call this together with wiping the store (e.g. on a sync-inception rebuild or "start fresh") so stale state from before the wipe can't leak into the replay.</summary>
    public void ResetSessionState()
    {
        _lastKnownPlaceId = null;
        _lastKnownSystem = null;
        _pendingFreight.Clear();
    }

    /// <summary>
    /// Applies a whole sequence of events under one transaction. Use this for any multi-event
    /// batch (a tick's worth of new lines, a whole backlog file) -- applying events one at a
    /// time outside a transaction means every write autocommits (and fsyncs) on its own, which
    /// dominates sync time once you're applying hundreds of thousands of events.
    /// </summary>
    /// <returns>Number of events applied.</returns>
    public int ApplyBatch(IEnumerable<InventoryEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        _store.BeginTransaction();
        try
        {
            var count = 0;
            foreach (var ev in events)
            {
                // Sync-inception lower bound: ignore anything older than the configured start date.
                if (InceptionUtc is { } inception && ev.Timestamp < inception)
                    continue;
                Apply(ev);
                count++;
            }
            _store.CommitTransaction();
            return count;
        }
        catch
        {
            _store.RollbackTransaction();
            throw;
        }
    }

    public void Apply(InventoryEvent ev)
    {
        ArgumentNullException.ThrowIfNull(ev);

        switch (ev)
        {
            case ItemMovedEvent move: ApplyMove(move); break;
            case ItemDroppedEvent dropped: ApplyDropped(dropped); break;
            case FreightInventoryEvent freight: ApplyFreightInventory(freight); break;
            case FreightDescendedEvent descent: ApplyFreightDescended(descent); break;
            case PlayerLocationEvent loc: ApplyPlayerLocation(loc); break;
            case EquippedItemEvent eq: ApplyEquipped(eq); break;
            case ContainerOpenedEvent open: ApplyOpened(open); break;
            case ContainerClosedEvent close: ApplyClosed(close); break;
            case StationIdentifiedEvent station: ApplyStation(station); break;
            case ContainerIdentifiedEvent container: ApplyContainerIdentified(container); break;
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

    // A dropped item leaves its source inventory and becomes a loose world entity -- the log carries
    // no destination inventory. Credit it to the flagged "Dropped" bucket and remember it briefly: if
    // a freight elevator is sent down within FreightMergeWindow, the drop was a station deposit and is
    // moved onto that station's inventory (see ApplyFreightDescended); otherwise it stays flagged as
    // Dropped -- a mission-site / ground drop, retained for later.
    private void ApplyDropped(ItemDroppedEvent dropped)
    {
        PrunePendingFreight(dropped.Timestamp);

        var itemId = _store.EnsureItem(dropped.ItemClass, _names.Resolve(dropped.ItemClass));
        var source = LocationKey(dropped.Source);

        _store.UpsertLocation(source, dropped.Timestamp, label: null);
        _store.AdjustHolding(source, itemId, -dropped.Quantity, dropped.Timestamp);

        _store.UpsertLocation(DroppedBucketId, dropped.Timestamp, DroppedLabel, DroppedSystem);
        _store.AdjustHolding(DroppedBucketId, itemId, +dropped.Quantity, dropped.Timestamp);

        _pendingFreight.Add((dropped.Timestamp, itemId, dropped.Quantity));
    }

    // The freight grid names the station's place id directly, so keep it as the current place -- a
    // following descent then knows where the freight lands. Mints no row of its own (the station-id
    // event is what labels the place; an unlabelled id here simply can't receive a merge below).
    private void ApplyFreightInventory(FreightInventoryEvent freight)
        => _lastKnownPlaceId = freight.PlaceId;

    // Freight went down: move every drop still inside the merge window from the Dropped bucket onto the
    // current station's inventory -- the same Location:placeId a move-to-locker uses, so a freight drop
    // reads exactly like an item moved into local storage ("considered like moving to the location
    // inventory"). Requires a *labelled* place (a station we've identified); without one the drops stay
    // flagged in the Dropped bucket rather than vanish onto an invisible row.
    private void ApplyFreightDescended(FreightDescendedEvent descent)
    {
        PrunePendingFreight(descent.Timestamp);
        if (_pendingFreight.Count == 0)
            return;
        if (_lastKnownPlaceId is not { } placeId || _store.GetLocation(placeId)?.Label is null)
            return;

        _store.UpsertLocation(placeId, descent.Timestamp, label: null);
        foreach (var (_, itemId, qty) in _pendingFreight)
        {
            _store.AdjustHolding(DroppedBucketId, itemId, -qty, descent.Timestamp);
            _store.AdjustHolding(placeId, itemId, +qty, descent.Timestamp);
        }
        _pendingFreight.Clear();
    }

    // A drop older than the merge window can no longer be claimed by a descent -- forget it (it stays
    // credited to the Dropped bucket).
    private void PrunePendingFreight(DateTimeOffset now)
        => _pendingFreight.RemoveAll(p => now - p.At > FreightMergeWindow);

    // A soft location hint from a loading platform (ship / freight elevator): no numeric place id, so
    // it can only (a) tag the player's current system and (b) reconnect to a place a prior
    // StationIdentifiedEvent already minted, matched by readable label. It never mints a place itself.
    // This is what pulls a freight-elevator drop out of "Other" and onto the station it happened at,
    // even when the player never opened that station's Local Inventory panel.
    private void ApplyPlayerLocation(PlayerLocationEvent loc)
    {
        var system = _systems.Resolve(loc.LocationToken);
        if (system != UnknownSystem)
            _lastKnownSystem = system;

        if (_store.FindPlaceByLabel(loc.LocationToken) is { } place)
        {
            _lastKnownPlaceId = place.Id;
            _lastKnownSystem = place.System ?? _lastKnownSystem;
        }
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
        // INVALID_LOCATION_ID is the game's sentinel for "no real place" -- never mint a labelled row
        // for it (it would surface as a bogus station with no system, i.e. an "Other" row).
        if (station.StationCode == "INVALID_LOCATION_ID")
            return;

        var label = _stations.Resolve(station.StationCode);
        var system = _systems.Resolve(station.StationCode);
        _store.UpsertLocation(station.PlaceId, station.Timestamp,
            string.IsNullOrEmpty(label) ? null : label, system);
        _lastKnownPlaceId = station.PlaceId;
        if (system != UnknownSystem)
            _lastKnownSystem = system;
    }

    // Labels the box's holdings key (its GEID) so its contents surface, and records the place it
    // sits in so it nests under that place. When the open line carried no parent location
    // (ParentLocationId == 0) we fall back to a bare label so the box still surfaces on its own.
    private void ApplyContainerIdentified(ContainerIdentifiedEvent container)
    {
        var label = $"Stor-All {container.ScuSize} SCU";
        if (container.ParentLocationId > 0)
            _store.UpsertContainer(container.ContainerId, container.ParentLocationId, container.Timestamp, label);
        else
            _store.UpsertLocation(container.ContainerId, container.Timestamp, label, _lastKnownSystem);
    }
}
