namespace AssetMemory.Core.Resolution;

/// <summary>Resolves a log <c>ItemClass</c> string into a human-readable display name.</summary>
public interface IItemNameResolver
{
    string Resolve(string? itemClass);
}

/// <summary>Pure transform that produces a fallback display name from an unknown <c>ItemClass</c>.</summary>
public interface IItemNameFormatter
{
    string Format(string? itemClass);
}
