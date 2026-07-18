using AssetMemory.Collector;
using AssetMemory.Core.Detection;
using AssetMemory.Data;

namespace AssetMemory.Tui;

/// <summary>
/// Sole-instance write path: the TUI hosts the collector itself, so every action mutates the local
/// DB/settings directly. Mirrors the equivalent logic in the Blazor UI's <c>Home.razor</c>.
/// </summary>
public sealed class LocalActions : IActions
{
    private readonly GameLogCollector _collector;
    private readonly SyncService _sync;
    private readonly AssetMemoryStore _writeStore;
    private readonly LogTailer _tailer;
    private readonly AppSettings _settings;
    private readonly string _settingsPath;

    public LocalActions(
        GameLogCollector collector,
        SyncService sync,
        AssetMemoryStore writeStore,
        LogTailer tailer,
        AppSettings settings,
        string settingsPath)
    {
        _collector = collector;
        _sync = sync;
        _writeStore = writeStore;
        _tailer = tailer;
        _settings = settings;
        _settingsPath = settingsPath;
    }

    public bool IsViewer => false;
    public bool IsInitialSyncing => !_collector.HasCompletedFirstTick;
    public string? GameLogPath => _settings.GameLogPath;

    public SyncResult Sync() => _sync.Sync();

    public void Clear()
    {
        _collector.StartFresh(() => _writeStore.ClearAll());
        _settings.ProcessedBackups.Clear();
        _settings.Save(_settingsPath);
    }

    public SetPathResult SetPath(string folder, bool startFresh)
    {
        var resolved = GamePathFinder.FindGameLogInFolder(folder);
        if (resolved is null && GamePathFinder.IsValidGameLog(folder))
            resolved = Path.GetFullPath(folder);
        if (resolved is null)
            return new SetPathResult(false, null, "Game.log not found at that location.");

        _settings.GameLogPath = resolved;
        _settings.Save(_settingsPath);
        _tailer.SetPath(resolved);
        if (startFresh)
            _tailer.SeekToEnd();
        return new SetPathResult(true, resolved, null);
    }
}
