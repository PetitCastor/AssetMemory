using System.Globalization;
using System.Text;

namespace AssetMemory.Collector;

/// <summary>
/// Incrementally reads a log file that another process is appending to. Handles SC's
/// launch-time truncation by detecting when the file is shorter than the tailer's
/// remembered position and rewinding to zero. Partial trailing lines (those not yet
/// terminated by a newline) are held back until they complete, so a parser never sees
/// half a line.
///
/// The read position is persisted to an optional small state file (<c>path\nposition</c>). Without it,
/// every process launch restarts at zero and re-applies the whole current Game.log over a DB the previous
/// session already populated — additively double-counting every holding. Restoring the position on launch
/// makes a restart resume exactly where it left off (and, conversely, not silently skip lines written
/// while the app was down). The stored path guards against restoring a stale offset onto a different file.
/// </summary>
public sealed class LogTailer : IDisposable
{
    private string _path;
    private long _position;
    private bool _disposed;
    private readonly string? _statePath;

    public LogTailer(string path, string? statePath = null)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _statePath = statePath;
        LoadState();
    }

    public string Path => _path;

    public void SetPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (_path != path)
        {
            _path = path;
            _position = 0;
            SaveState();
        }
    }

    public long Position => _position;

    public void Reset()
    {
        _position = 0;
        SaveState();
    }

    public void SeekToEnd()
    {
        if (File.Exists(_path))
            _position = new FileInfo(_path).Length;
        SaveState();
    }

    public IEnumerable<string> ReadNew()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!File.Exists(_path))
        {
            // Nothing yet; do not advance. When the file appears we'll pick it up from zero.
            return [];
        }

        var info = new FileInfo(_path);
        if (info.Length < _position)
        {
            // Truncation (e.g., game relaunched). Replay from the top.
            _position = 0;
            SaveState();
        }
        if (info.Length == _position)
        {
            return [];
        }

        var lines = new List<string>();

        // FileShare.ReadWrite is essential — Star Citizen holds Game.log open for writing
        // while we read. Anything stricter would either fail to open or block the game.
        using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        fs.Seek(_position, SeekOrigin.Begin);
        using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);

        var buffer = sr.ReadToEnd();

        // Find the last terminator. Bytes past that are an incomplete line — leave them for next tick.
        int lastTerminator = -1;
        for (int i = buffer.Length - 1; i >= 0; i--)
        {
            if (buffer[i] == '\n')
            {
                lastTerminator = i;
                break;
            }
        }

        if (lastTerminator < 0)
        {
            // No complete lines yet — wait for more.
            return [];
        }

        var complete = buffer[..(lastTerminator + 1)];
        foreach (var raw in complete.Split('\n'))
        {
            var line = raw.EndsWith('\r') ? raw[..^1] : raw;
            if (line.Length > 0)
                lines.Add(line);
        }

        // Advance by the byte count of what we consumed (terminator included).
        _position += Encoding.UTF8.GetByteCount(complete);
        SaveState();

        return lines;
    }

    // Restores a persisted position, but only when the state file records the SAME path — a stale offset
    // for a different Game.log would be meaningless (ReadNew's truncation check also guards it).
    private void LoadState()
    {
        if (_statePath is null || !File.Exists(_statePath))
            return;
        try
        {
            var text = File.ReadAllText(_statePath);
            var nl = text.IndexOf('\n');
            if (nl <= 0)
                return;
            var savedPath = text[..nl].TrimEnd('\r');
            if (savedPath == _path
                && long.TryParse(text[(nl + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pos)
                && pos >= 0)
                _position = pos;
        }
        catch
        {
            // Corrupt/unreadable state — fall back to a full re-read from zero.
        }
    }

    // Best-effort: a lost write just means the next launch re-reads a little (or skips nothing on a
    // resume), never corruption. Tiny file, so writing it each advancing tick is cheap.
    private void SaveState()
    {
        if (_statePath is null)
            return;
        try
        {
            File.WriteAllText(_statePath, $"{_path}\n{_position.ToString(CultureInfo.InvariantCulture)}");
        }
        catch
        {
            // Non-fatal: persistence is an optimisation, not a correctness requirement within a session.
        }
    }

    public void Dispose() => _disposed = true;
}
