using AssetMemory.Collector;
using AssetMemory.Collector.Control;

namespace AssetMemory.Tui;

/// <summary>
/// Viewer-mode write path: a separate process owns the collector, so writes are delegated to it over
/// the named-pipe control channel. Works regardless of whether that owner is the web/tray host or
/// another (sole-instance) console TUI — both serve the same pipe.
/// </summary>
public sealed class PipeActions : IActions
{
    private readonly ControlPipeClient _client;
    private string? _gameLogPath;

    public PipeActions(ControlPipeClient client, string? gameLogPath)
    {
        _client = client;
        _gameLogPath = gameLogPath;
    }

    public bool IsViewer => true;
    public bool IsInitialSyncing => false;
    public string? GameLogPath => _gameLogPath;

    public SyncResult Sync() => _client.Sync();

    public void Clear() => _client.Clear();

    public SetPathResult SetPath(string folder, bool startFresh)
    {
        var r = _client.SetPath(folder, startFresh);
        if (r.Ok)
            _gameLogPath = r.ResolvedPath;
        return new SetPathResult(r.Ok, r.ResolvedPath, r.Error);
    }
}
