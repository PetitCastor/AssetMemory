using AssetMemory.Collector;
using AssetMemory.Collector.Control;

namespace AssetMemory.Tui;

/// <summary>
/// Sole-instance write path: the TUI hosts the collector itself and mutates through the shared
/// <see cref="ControlService"/> (the same code the pipe server dispatches for viewers).
/// </summary>
public sealed class LocalActions : IActions
{
    private readonly ControlService _control;
    private readonly GameLogCollector _collector;

    public LocalActions(ControlService control, GameLogCollector collector)
    {
        _control = control;
        _collector = collector;
    }

    public bool IsViewer => false;
    public bool IsInitialSyncing => !_collector.HasCompletedFirstTick;
    public string? GameLogPath => _control.GameLogPath;
    public DateTimeOffset? InceptionUtc => _control.Inception;

    public SyncResult Sync() => _control.Sync();

    public void Clear() => _control.Clear();

    public SetPathResult SetPath(string folder, bool startFresh)
    {
        var r = _control.SetPath(folder, startFresh);
        return new SetPathResult(r.Ok, r.ResolvedPath, r.Error);
    }

    public void SetInception(DateTimeOffset? date) => _control.SetInception(date);
}
