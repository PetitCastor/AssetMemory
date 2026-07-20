namespace AssetMemory.Core.Resolution;

/// <summary>Resolves a log station code (e.g. <c>Stanton4_NewBabbage</c>) to its starmap system.</summary>
public interface ISystemNameResolver
{
    string Resolve(string? stationCode);
}
