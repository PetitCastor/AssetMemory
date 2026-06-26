namespace AssetMemory.Core.Resolution;

/// <summary>Resolves a log station code (e.g. <c>Stanton4_NewBabbage</c>) into a human-readable name.</summary>
public interface IStationNameResolver
{
    string Resolve(string? stationCode);
}

/// <summary>Pure transform that produces a readable name from an unknown station code.</summary>
public interface IStationNameFormatter
{
    string Format(string? stationCode);
}
