using System.Text.Json;
using AssetMemory.Core.Detection;
using AssetMemory.Data;

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
    private readonly AppSettings _settings;
    private readonly string _settingsPath;
    private readonly string _dbPath;

    public ControlService(
        GameLogCollector collector,
        SyncService sync,
        AssetMemoryStore store,
        LogTailer tailer,
        AppSettings settings,
        string settingsPath,
        string dbPath)
    {
        _collector = collector;
        _sync = sync;
        _store = store;
        _tailer = tailer;
        _settings = settings;
        _settingsPath = settingsPath;
        _dbPath = dbPath;
    }

    public string? GameLogPath => _settings.GameLogPath;

    public ControlInfo Info() => new(_dbPath, _settings.GameLogPath);

    public SyncResult Sync() => _sync.Sync();

    public void Clear()
    {
        _collector.StartFresh(() => _store.ClearAll());
        _settings.ProcessedBackups.Clear();
        _settings.Save(_settingsPath);
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
            _ => new ControlSetPathResult(false, null, $"unknown op '{req.Op}'"),
        };
        return JsonSerializer.Serialize(response, ControlProtocol.Json);
    }

    private ControlOk ClearAndAck()
    {
        Clear();
        return new ControlOk(true);
    }
}
