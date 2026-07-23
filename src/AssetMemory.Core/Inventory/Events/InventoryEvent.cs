namespace AssetMemory.Core.Inventory.Events;

/// <summary>Base type for a meaningful inventory event recovered from the game log.</summary>
public abstract record InventoryEvent(DateTimeOffset Timestamp);

/// <summary>A container/location inventory was opened (the box, ship or local storage).</summary>
public sealed record ContainerOpenedEvent(
    DateTimeOffset Timestamp,
    string Player,
    InventoryRef Container,
    string ContainerClass,
    int RequestId) : InventoryEvent(Timestamp);

/// <summary>A container/location inventory UI was closed.</summary>
public sealed record ContainerClosedEvent(
    DateTimeOffset Timestamp,
    string Player,
    InventoryRef Container) : InventoryEvent(Timestamp);

/// <summary>An item stack was moved between inventories (the only event that carries identity + true quantity).</summary>
public sealed record ItemMovedEvent(
    DateTimeOffset Timestamp,
    string Player,
    string ItemClass,
    int Quantity,
    InventoryRef Source,
    InventoryRef Target,
    int RequestId) : InventoryEvent(Timestamp);

/// <summary>
/// A bulk "Move all" whose moved items the game logs ONLY as a bracketed list on a single
/// <c>&lt;Add Inventory Management Move&gt; New request[N] Type[Move] … ItemClass[[c1] [c2] …]</c>
/// line — the paired committed <c>Queued Request[N]</c> carries <c>Source[NULL] amount[0]</c> and there
/// are no per-item <c>Queued</c> lines, so <see cref="ItemMovedEvent"/>'s per-item gate never sees the
/// items. This is how a backpack/box → station (and station → box) "Move all" is logged. The applier
/// moves each listed class wholesale (its full known balance at <see cref="Source"/>, falling back to 1
/// when that source was never tracked) from <see cref="Source"/> to <see cref="Target"/>. Quantity is
/// not in the log, so a stack whose source balance is unknown is surfaced as a single unit.
/// </summary>
public sealed record ItemsMovedBatchEvent(
    DateTimeOffset Timestamp,
    string Player,
    IReadOnlyList<string> ItemClasses,
    InventoryRef Source,
    InventoryRef Target,
    int RequestId) : InventoryEvent(Timestamp);

/// <summary>
/// An item was dropped out of an inventory into the physical world (e.g. onto the ground, a
/// freight elevator platform, or anywhere else with no inventory of its own) rather than moved to
/// another inventory. The game logs this as <c>Type[Drop]</c> with <c>TargetInventory[INVALID]</c>,
/// so unlike <see cref="ItemMovedEvent"/> there is no destination inventory ref -- <see cref="EntityId"/>
/// (the spawned world entity, reported by the paired <c>UnstowPendingEntities</c> line) stands in for
/// one, so the drop still gets a place of its own to be tracked at instead of vanishing.
/// </summary>
public sealed record ItemDroppedEvent(
    DateTimeOffset Timestamp,
    string Player,
    string ItemClass,
    int Quantity,
    InventoryRef Source,
    long EntityId,
    int RequestId) : InventoryEvent(Timestamp);

/// <summary>The stack count reported when a container is opened (count of stacks, not units).</summary>
public sealed record GridItemCountEvent(
    DateTimeOffset Timestamp,
    int RequestId,
    int StackCount,
    int InventoryCount) : InventoryEvent(Timestamp);

/// <summary>An item attached to the player's body ports — i.e. the equipped loadout.</summary>
public sealed record EquippedItemEvent(
    DateTimeOffset Timestamp,
    string Player,
    string ItemClass,
    string InstanceName,
    long EntityId,
    string Port,
    string Status) : InventoryEvent(Timestamp);

/// <summary>
/// An item was equipped straight onto a body port out of an inventory (a box, a station locker),
/// which the game logs as <c>&lt;EquipItem&gt; equip from Inventory[ref] Class[class] Port[port]</c>.
/// Unlike a locker-to-locker <see cref="ItemMovedEvent"/> this is a <c>Type[Interaction]</c>, so it
/// is missed by <see cref="ItemMovedEvent"/>'s <c>Type[Move]</c> gate — yet the item genuinely leaves
/// its source inventory. The applier debits one unit from <see cref="Source"/>; the equipped-loadout
/// side is recorded separately by the <see cref="EquippedItemEvent"/> the paired
/// <c>&lt;AttachmentReceived&gt;</c> line produces. Equipping attaches exactly one item to a port, so
/// there is no quantity to carry.
/// </summary>
public sealed record ItemEquippedFromInventoryEvent(
    DateTimeOffset Timestamp,
    string ItemClass,
    InventoryRef Source,
    string Port,
    int RequestId) : InventoryEvent(Timestamp);

/// <summary>
/// An item was stored straight into an inventory (a box, a backpack, a station locker) out of the
/// player's hands or equipped loadout — the mirror of <see cref="ItemEquippedFromInventoryEvent"/>.
/// The game logs a store as <c>Type[Store]</c>, not <c>Type[Move]</c>, so <see cref="ItemMovedEvent"/>'s
/// gate misses it and the destination is never credited. The applier credits one unit to
/// <see cref="Target"/>. Storing one entity is quantity 1; a stack stores as one such event per unit
/// (each unit carries its own entity id on its own committed line).
/// </summary>
public sealed record ItemStoredEvent(
    DateTimeOffset Timestamp,
    string ItemClass,
    InventoryRef Target,
    int RequestId) : InventoryEvent(Timestamp);

/// <summary>
/// A station's persistent local inventory was opened, tying its numeric <see cref="PlaceId"/>
/// (the 3rd field of a <c>GEID:Location:placeId</c> ref) to a readable <see cref="StationCode"/>
/// (e.g. <c>Stanton4_NewBabbage</c>). Recovered by pairing a <c>RequestLocationInventory</c> line
/// with the <c>RequestInventory</c> line that immediately follows it.
/// </summary>
public sealed record StationIdentifiedEvent(
    DateTimeOffset Timestamp,
    string Player,
    long PlaceId,
    string StationCode) : InventoryEvent(Timestamp);

/// <summary>
/// A nested storage container (a "Stor-All" SCU box) was opened, tying its persistent
/// <see cref="ContainerId"/> (the GEID that owns the <c>GEID:Container:0</c> ref its holdings key
/// off) to the class it was placed from (e.g. <c>Carryable_TBO_InventoryContainer_2SCU</c>) and the
/// derived <see cref="ScuSize"/>. Recovered by pairing an <c>OpenNestedInventory</c> request (carries
/// the class) with the <c>Query Inventory</c> line that reports the box's numeric container ref.
/// <see cref="ParentLocationId"/> is the place the box sits at (the <c>:Location:</c> id on the open
/// line's <c>Source Inventory</c>), so the box can nest under that place; <c>0</c> when unknown.
/// </summary>
public sealed record ContainerIdentifiedEvent(
    DateTimeOffset Timestamp,
    long ContainerId,
    string ContainerClass,
    int ScuSize,
    long ParentLocationId) : InventoryEvent(Timestamp);

/// <summary>
/// The player rode or summoned a loading platform (a ship or freight elevator) at a station. The
/// platform-manager name carries a trailing location token -- a landing-zone place name (e.g.
/// <c>Levski</c>) or, on a hangar elevator, a bare system name (e.g. <c>Nyx</c>). Unlike
/// <see cref="StationIdentifiedEvent"/> this carries no <c>GEID:Location:placeId</c>, so on its own
/// it can't mint a place; it is a soft "player is here" hint the applier uses to tag the current
/// system and to reconnect to a place a prior <see cref="StationIdentifiedEvent"/> already minted
/// (matched by label). That lets a drop made before -- or entirely without -- the Local Inventory
/// panel ever being opened still land at the right station instead of a system-less "Other" row.
/// </summary>
public sealed record PlayerLocationEvent(
    DateTimeOffset Timestamp,
    string LocationToken) : InventoryEvent(Timestamp);

/// <summary>
/// The player interacted with a station's freight-elevator inventory grid, which the game logs as
/// <c>Freight Inventory Grid Requesting Inventory[GEID:Location:&lt;placeId&gt;]</c>. The freight
/// grid shares its <see cref="PlaceId"/> with the station's own local inventory (same
/// <c>Location:placeId</c> key a move-to-locker uses), so this is a hard, numeric confirmation of
/// which station the player is standing at right now — used to keep the applier's "current place"
/// fresh so a following freight descent can attribute the just-dropped items to it.
/// </summary>
public sealed record FreightInventoryEvent(
    DateTimeOffset Timestamp,
    long PlaceId) : InventoryEvent(Timestamp);

/// <summary>
/// A freight elevator was sent down — the game logs the loading-platform state
/// <c>changed to LoweringPlatform</c> on a <c>FreightElevator</c> manager. This is the "freight goes
/// down" moment: any items dropped onto that platform shortly before are now delivered to the
/// station, so the applier moves them from the transient "Dropped" bucket onto the current station's
/// location inventory. <see cref="LocationToken"/> is the platform-manager's trailing token (a place
/// like <c>Levski</c> or a bare system like <c>Nyx</c>); the place id itself comes from the applier's
/// current-place state, not this token.
/// </summary>
public sealed record FreightDescendedEvent(
    DateTimeOffset Timestamp,
    string LocationToken) : InventoryEvent(Timestamp);
