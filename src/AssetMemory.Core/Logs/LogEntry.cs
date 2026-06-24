namespace AssetMemory.Core.Logs;

/// <summary>
/// A single parsed Star Citizen <c>Game.log</c> line, split into its envelope
/// (<see cref="Timestamp"/>, <see cref="Severity"/>, <see cref="Category"/>) and the
/// remaining <see cref="Message"/> body. <see cref="Raw"/> is the original text.
/// </summary>
public sealed record LogEntry(
    DateTimeOffset Timestamp,
    string Severity,
    string Category,
    string Message,
    string Raw);
