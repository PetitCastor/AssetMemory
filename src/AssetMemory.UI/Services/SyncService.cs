using AssetMemory.Collector;
using AssetMemory.Core.Detection;

namespace AssetMemory.UI.Services;

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

            // 1. Re-read current Game.log from the beginning
            var logPath = _settings.GameLogPath;
            if (!string.IsNullOrEmpty(logPath) && File.Exists(logPath))
            {
                var count = _collector.ProcessFile(logPath);
                totalEvents += count;
                filesProcessed++;
                _logger.LogInformation("Sync: processed current Game.log, {Count} events", count);
            }

            // 2. Process backup logs
            var backupDir = FindBackupDir(logPath);
            if (backupDir is not null && Directory.Exists(backupDir))
            {
                var backupFiles = Directory.GetFiles(backupDir, "*.log")
                    .OrderBy(f => f)
                    .ToList();

                foreach (var backup in backupFiles)
                {
                    var name = Path.GetFileName(backup);
                    if (_settings.ProcessedBackups.Contains(name))
                        continue;

                    try
                    {
                        var count = _collector.ProcessFile(backup);
                        totalEvents += count;
                        filesProcessed++;
                        _settings.ProcessedBackups.Add(name);
                        _logger.LogInformation("Sync: processed backup {Name}, {Count} events", name, count);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Sync: failed to process backup {Name}", name);
                    }
                }

                _settings.Save(_settingsPath);
            }

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
