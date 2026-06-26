using AssetMemory.Core.Inventory;
using AssetMemory.Data.Events;

namespace AssetMemory.Collector;

/// <summary>
/// Drives the pipeline: drain new lines from a <see cref="LogTailer"/>, parse them via
/// <see cref="InventoryLogReader"/>, then push each event through an <see cref="EventApplier"/>.
/// Stateless beyond its dependencies — schedule <see cref="Tick"/> on whatever interval suits.
/// </summary>
public sealed class GameLogCollector
{
    private readonly LogTailer _tailer;
    private readonly EventApplier _applier;
    private readonly InventoryLogReader _reader;
    private readonly Lock _lock = new();

    public bool HasCompletedFirstTick { get; private set; }

    public GameLogCollector(LogTailer tailer, EventApplier applier, InventoryLogReader? reader = null)
    {
        _tailer = tailer ?? throw new ArgumentNullException(nameof(tailer));
        _applier = applier ?? throw new ArgumentNullException(nameof(applier));
        _reader = reader ?? new InventoryLogReader();
    }

    /// <returns>Number of inventory events applied this tick.</returns>
    public int Tick()
    {
        lock (_lock)
        {
            var count = _applier.ApplyBatch(_reader.Read(_tailer.ReadNew()));
            HasCompletedFirstTick = true;
            return count;
        }
    }

    public void StartFresh(Action clearData)
    {
        lock (_lock)
        {
            clearData();
            _tailer.SeekToEnd();
        }
    }

    public int ProcessFile(string path)
    {
        lock (_lock)
        {
            return _applier.ApplyBatch(_reader.Read(ReadLinesShared(path)));
        }
    }

    private static IEnumerable<string> ReadLinesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
            yield return line;
    }
}
