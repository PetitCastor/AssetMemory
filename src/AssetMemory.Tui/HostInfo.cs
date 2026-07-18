using System.Net.Http.Json;

namespace AssetMemory.Tui;

/// <summary>
/// The bits of runtime state the TUI needs from a running background app when it attaches in viewer
/// mode — chiefly the DB path, since the TUI is a different exe with a different base directory and
/// must open the <em>host's</em> database, not one next to itself.
/// </summary>
public sealed record HostInfo(string DbPath, string? GameLogPath)
{
    public static HostInfo Fetch(string baseUrl)
    {
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(3) };
        var dto = http.GetFromJsonAsync<InfoDto>("/api/info").GetAwaiter().GetResult()
                  ?? throw new InvalidOperationException("Empty /api/info response");
        return new HostInfo(dto.DbPath, dto.GameLogPath);
    }

    private sealed record InfoDto(string DbPath, string? GameLogPath);
}
