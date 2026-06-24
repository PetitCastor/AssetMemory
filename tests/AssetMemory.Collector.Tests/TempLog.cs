using System.Text;

namespace AssetMemory.Collector.Tests;

/// <summary>
/// Disposable temp file for tailer tests. Writes append with the same FileShare flags the
/// game would use; the tailer must open shared-read-write so this works concurrently.
/// </summary>
internal sealed class TempLog : IDisposable
{
    public string Path { get; }

    public TempLog()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"assetmemory-test-{Guid.NewGuid():N}.log");
        File.WriteAllBytes(Path, []);
    }

    public void Append(params string[] lines)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Concat(lines.Select(l => l + "\r\n")));
        using var fs = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        fs.Write(bytes);
    }

    /// <summary>Replace the file's contents entirely (simulates SC's launch-time truncation).</summary>
    public void Truncate()
    {
        using var fs = new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
    }

    public void Dispose()
    {
        try { File.Delete(Path); } catch { /* best effort */ }
    }
}
