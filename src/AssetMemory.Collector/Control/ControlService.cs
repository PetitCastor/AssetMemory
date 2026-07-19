using System.Text.Json;
using AssetMemory.Core.Detection;
using AssetMemory.Data;
using AssetMemory.Data.Events;

namespace AssetMemory.Collector.Control;

/// <summary>
/// The collector-owning process's write operations (info/sync/clear/set-path) in one place. Used
/// three ways: directly by the console TUI in sole-instance mode, dispatched over the named pipe by
/// <see cref="ControlPipeServer"/> for viewers, and (indirectly) it centralises the same logic the
/// Blazor UI performs inline. All DB mutations flow through <see cref="GameLogCollector"/>'s lock, so
/// they stay serialized against the background tick on the shared write connection.
/// </summary>
public sealed class ControlService
{
    private readonly GameLogCollector _collector;
    private readonly SyncService _sync;
    private readonly AssetMemoryStore _store;
    private readonly LogTailer _tailer;
    private readonly EventApplier _applier;
    private readonly AppSettings _settings;
    private readonly string _settingsPath;
    private readonly string _dbPath;

    public ControlService(
        GameLogCollector collector,
        SyncService sync,
        AssetMemoryStore store,
        LogTailer tailer,
        EventApplier applier,
        AppSettings settings,
        string settingsPath,
        string dbPath)
    {
        _collector = collector;
        _sync = sync;
        _store = store;
        _tailer = tailer;
        _applier = applier;
        _settings = settings;
        _settingsPath = settingsPath;
        _dbPath = dbPath;
    }

    public string? GameLogPath => _settings.GameLogPath;
    public DateTimeOffset? Inception => _settings.SyncInceptionUtc;

    public ControlInfo Info() => new(_dbPath, _settings.GameLogPath, _settings.SyncInceptionUtc);

    public SyncResult Sync() => _sync.Sync();

    public void Clear()
    {
        _collector.StartFresh(() => _store.ClearAll());
        _settings.ProcessedBackups.Clear();
        _settings.Save(_settingsPath);
    }

    /// <summary>
    /// Sets (or clears, when <paramref name="date"/> is null) the sync-inception lower bound and rebuilds:
    /// retunes the shared applier, then clears and re-reads the log so pre-date holdings drop out and the
    /// remaining events re-apply under the new bound. <see cref="Clear"/> seeks the live tailer to end so
    /// the background tick won't double-count what <see cref="Sync"/> replays from the top.
    /// </summary>
    public void SetInception(DateTimeOffset? date)
    {
        _settings.SyncInceptionUtc = date;
        _applier.InceptionUtc = date;
        _settings.Save(_settingsPath);
        Clear();
        Sync();
    }

    public ControlSetPathResult SetPath(string folder, bool startFresh)
    {
        var resolved = GamePathFinder.FindGameLogInFolder(folder);
        if (resolved is null && GamePathFinder.IsValidGameLog(folder))
            resolved = Path.GetFullPath(folder);
        if (resolved is null)
            return new ControlSetPathResult(false, null, "Game.log not found at that location.");

        _settings.GameLogPath = resolved;
        _settings.Save(_settingsPath);
        _tailer.SetPath(resolved);
        if (startFresh)
            _tailer.SeekToEnd();
        return new ControlSetPathResult(true, resolved, null);
    }

    /// <summary>Server-side dispatch: parse a request line, run it, return a response line.</summary>
    public string Handle(string requestJson)
    {
        var req = JsonSerializer.Deserialize<ControlRequest>(requestJson, ControlProtocol.Json)
                  ?? new ControlRequest("");
        object response = req.Op switch
        {
            "info" => Info(),
            "sync" => Sync(),
            "clear" => ClearAndAck(),
            "setpath" => SetPath(req.Folder ?? "", req.StartFresh),
            "setinception" => SetInceptionAck(req.Inception),
            _ => new ControlSetPathResult(false, null, $"unknown op '{req.Op}'"),
        };
        return JsonSerializer.Serialize(response, ControlProtocol.Json);
    }

    private ControlOk ClearAndAck()
    {
        Clear();
        return new ControlOk(true);
    }

    private ControlOk SetInceptionAck(DateTimeOffset? date)
    {
        SetInception(date);
        return new ControlOk(true);
    }
}
