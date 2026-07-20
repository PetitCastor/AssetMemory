using System.Text.Json;

namespace AssetMemory.Collector;

/// <summary>
/// Thin wrapper over api.star-citizen.wiki's exact <c>class_name</c> search, used to backfill item
/// display names global.ini has no localization key for. Non-fatal by design: any failure (offline,
/// API down, malformed response, unmatched class) returns <c>null</c> so the caller just keeps the
/// heuristic name.
/// </summary>
public sealed class ExternalItemNameClient : IDisposable
{
    private readonly HttpClient _http;

    public ExternalItemNameClient(HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri("https://api.star-citizen.wiki/api/search/");
        _http.Timeout = TimeSpan.FromSeconds(10);
        _http.DefaultRequestHeaders.Accept.Add(new("application/json"));
    }

    public async Task<string?> TryResolveAsync(string itemClass, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(Uri.EscapeDataString(itemClass), ct);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return doc.RootElement.TryGetProperty("data", out var data)
                && data.TryGetProperty("name", out var name)
                ? name.GetString()
                : null;
        }
        catch (Exception)
        {
            return null; // offline / API down / malformed response — non-fatal, caller keeps the heuristic name
        }
    }

    public void Dispose() => _http.Dispose();
}
