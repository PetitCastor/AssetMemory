namespace AssetMemory.Data;

public sealed record LocationRow(long Id, string? Label, DateTimeOffset LastSeenUtc);

public sealed record ItemRow(long Id, string ClassName, string? DisplayName);

public sealed record HoldingRow(
    long LocationId,
    long ItemId,
    int Quantity,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc);

public sealed record EquippedRow(
    string Player,
    string Port,
    long ItemId,
    long EntityId,
    string? InstanceName,
    string? Status,
    DateTimeOffset LastSeenUtc);

public sealed record AuditRow(long Id, DateTimeOffset Utc, string Type, string Raw);

public sealed record HoldingDetail(
    long LocationId,
    string? LocationLabel,
    string? LocationParentLabel,
    long ItemId,
    string ItemClassName,
    string? ItemDisplayName,
    int Quantity,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc);

public sealed record HoldingDetailsPage(
    IReadOnlyList<HoldingDetail> Rows,
    int TotalCount,
    int DistinctLocations,
    long TotalUnits);

public sealed record ItemLocationDetail(
    long LocationId,
    string? LocationLabel,
    int Quantity,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc);

public sealed record EquippedDetail(
    string Player,
    string Port,
    long ItemId,
    string ItemClassName,
    string? ItemDisplayName,
    long EntityId,
    string? InstanceName,
    string? Status,
    DateTimeOffset LastSeenUtc);
