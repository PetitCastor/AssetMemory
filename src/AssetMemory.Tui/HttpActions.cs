using System.Net.Http.Json;
using AssetMemory.Collector;

namespace AssetMemory.Tui;

/// <summary>
/// Viewer-mode write path: a separate background process owns the collector, so writes are delegated
/// to its localhost control endpoints (see the web host's <c>/api/*</c> routes). This keeps the
/// collector's in-memory state (tail position, first-tick flag) consistent with the DB.
/// </summary>
public sealed class HttpActions : IActions
{
    private readonly HttpClient _http;
    private string? _gameLogPath;

    public HttpActions(string baseUrl, string? gameLogPath)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
        _gameLogPath = gameLogPath;
    }

    public bool IsViewer => true;
    public bool IsInitialSyncing => false;
    public string? GameLogPath => _gameLogPath;

    public SyncResult Sync()
    {
        var resp = _http.PostAsync("/api/sync", content: null).GetAwaiter().GetResult();
        resp.EnsureSuccessStatusCode();
        return resp.Content.ReadFromJsonAsync<SyncResult>().GetAwaiter().GetResult()
               ?? new SyncResult(0, 0, "No response");
    }

    public void Clear()
    {
        var resp = _http.PostAsync("/api/clear", content: null).GetAwaiter().GetResult();
        resp.EnsureSuccessStatusCode();
    }

    public SetPathResult SetPath(string folder, bool startFresh)
    {
        var resp = _http.PostAsJsonAsync("/api/path", new { Folder = folder, StartFresh = startFresh })
            .GetAwaiter().GetResult();
        if (!resp.IsSuccessStatusCode)
            return new SetPathResult(false, null, "Game.log not found at that location.");

        var body = resp.Content.ReadFromJsonAsync<PathResp>().GetAwaiter().GetResult();
        _gameLogPath = body?.Path;
        return new SetPathResult(true, body?.Path, null);
    }

    private sealed record PathResp(string Path);
}
