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
/// </summary>
public sealed record ContainerIdentifiedEvent(
    DateTimeOffset Timestamp,
    long ContainerId,
    string ContainerClass,
    int ScuSize) : InventoryEvent(Timestamp);
