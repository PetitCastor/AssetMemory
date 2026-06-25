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

/// <summary>A player handle observed alongside their GEID — used to label their personal inventory location.</summary>
public sealed record PlayerIdentityEvent(
    DateTimeOffset Timestamp,
    string Player,
    long Geid) : InventoryEvent(Timestamp);
