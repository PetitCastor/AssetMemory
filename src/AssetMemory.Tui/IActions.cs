using AssetMemory.Collector;

namespace AssetMemory.Tui;

/// <summary>Result of a "change game folder" request.</summary>
public sealed record SetPathResult(bool Ok, string? ResolvedPath, string? Error);

/// <summary>
/// The write-side operations the TUI can trigger. Two implementations back it: <see cref="LocalActions"/>
/// (sole-instance mode — the TUI owns the collector and mutates directly) and <see cref="HttpActions"/>
/// (viewer mode — a background app owns the collector, so writes are delegated to it over HTTP). Keeping
/// this behind one interface means the window code never branches on which mode it's in.
/// </summary>
public interface IActions
{
    /// <summary>True when attached to a separate collector-owning process (writes are delegated).</summary>
    bool IsViewer { get; }

    /// <summary>True while the owned collector hasn't finished its first tick (sole-instance startup only).</summary>
    bool IsInitialSyncing { get; }

    /// <summary>The Game.log currently being watched, or null if not yet configured.</summary>
    string? GameLogPath { get; }

    SyncResult Sync();
    void Clear();
    SetPathResult SetPath(string folder, bool startFresh);
}
