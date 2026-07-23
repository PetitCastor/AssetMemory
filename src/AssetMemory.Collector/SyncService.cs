using System.Globalization;
using AssetMemory.Core.Detection;
using Microsoft.Extensions.Logging;

namespace AssetMemory.Collector;

/// <summary>
/// Re-reads the current Game.log and folds in any not-yet-processed backup logs, applying every
/// parsed event through the collector. Shared by both front-ends (Blazor UI and the console TUI) so
/// the "sync backups" action behaves identically regardless of presentation.
///
/// Files are replayed strictly oldest-first (by their first log timestamp), because the holdings
/// ledger is order-sensitive: a debit clamps at zero (see <c>AssetMemoryStore.AdjustHolding</c>), so
/// an equip-out / move / drop / store-out in a later session that is applied before the earlier credit
/// that first put the item there silently no-ops — and the stale credit then wins. The current
/// Game.log is the newest (active) session and backups are older, but a plain filename sort is not
/// reliably chronological across months, so every file is ordered by the timestamp of its first entry.
/// </summary>
public sealed class SyncService
{
    private readonly GameLogCollector _collector;
    private readonly AppSettings _settings;
    private readonly string _settingsPath;
    private readonly ILogger<SyncService> _logger;
    private readonly Lock _syncLock = new();

    public bool IsSyncing { get; private set; }
    public string? LastSyncResult { get; private set; }

    public SyncService(
        GameLogCollector collector,
        AppSettings settings,
        string settingsPath,
        ILogger<SyncService> logger)
    {
        _collector = collector;
        _settings = settings;
        _settingsPath = settingsPath;
        _logger = logger;
    }

    public SyncResult Sync()
    {
        lock (_syncLock)
        {
            if (IsSyncing)
                return new SyncResult(0, 0, "Sync already in progress");

            IsSyncing = true;
        }

        try
        {
            var totalEvents = 0;
            var filesProcessed = 0;

            // Gather every source to replay this pass: the current Game.log plus any not-yet-processed
            // backups. The current log is not tracked in ProcessedBackups — it is always re-read.
            var logPath = _settings.GameLogPath;
            var sources = new List<(string Path, bool IsBackup)>();
            if (!string.IsNullOrEmpty(logPath) && File.Exists(logPath))
                sources.Add((logPath, false));

            var backupDir = FindBackupDir(logPath);
            if (backupDir is not null && Directory.Exists(backupDir))
            {
                foreach (var backup in Directory.GetFiles(backupDir, "*.log"))
                {
                    if (!_settings.ProcessedBackups.Contains(Path.GetFileName(backup)))
                        sources.Add((backup, true));
                }
            }

            // Replay oldest-first so debits never run ahead of the credits they depend on.
            var ordered = sources
                .Select(s => (s.Path, s.IsBackup, Start: FileStartTime(s.Path)))
                .OrderBy(s => s.Start)
                .ToList();

            var backupsChanged = false;
            foreach (var (path, isBackup, _) in ordered)
            {
                var name = Path.GetFileName(path);
                try
                {
                    var count = _collector.ProcessFile(path);
                    totalEvents += count;
                    filesProcessed++;
                    if (isBackup)
                    {
                        _settings.ProcessedBackups.Add(name);
                        backupsChanged = true;
                    }
                    _logger.LogInformation("Sync: processed {Name}, {Count} events", name, count);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Sync: failed to process {Name}", name);
                }
            }

            if (backupsChanged)
                _settings.Save(_settingsPath);

            LastSyncResult = $"Processed {filesProcessed} file(s), {totalEvents} events";
            return new SyncResult(filesProcessed, totalEvents, LastSyncResult);
        }
        finally
        {
            lock (_syncLock)
            {
                IsSyncing = false;
            }
        }
    }

    // The timestamp of a log file's first entry, used to replay files oldest-first. Reads only the head
    // of the file (a run of noise before the first "<timestamp>" line is skipped). Returns MaxValue when
    // no timestamp is found so an unreadable file sorts last rather than jumping ahead of real, older data.
    private static DateTimeOffset FileStartTime(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            for (var i = 0; i < 50 && reader.ReadLine() is { } line; i++)
            {
                if (line.Length > 1 && line[0] == '<')
                {
                    var end = line.IndexOf('>');
                    if (end > 1 && DateTimeOffset.TryParse(
                            line.AsSpan(1, end - 1), CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var ts))
                        return ts;
                }
            }
        }
        catch
        {
            // Unreadable file -> fall through to the sentinel so it sorts last.
        }

        return DateTimeOffset.MaxValue;
    }

    private static string? FindBackupDir(string? gameLogPath)
    {
        if (string.IsNullOrEmpty(gameLogPath))
            return null;
        var parent = Path.GetDirectoryName(gameLogPath);
        if (parent is null)
            return null;
        return Path.Combine(parent, "logbackups");
    }
}

public record SyncResult(int FilesProcessed, int TotalEvents, string Message);
