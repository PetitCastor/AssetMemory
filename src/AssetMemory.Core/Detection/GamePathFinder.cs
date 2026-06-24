namespace AssetMemory.Core.Detection;

public static class GamePathFinder
{
    private static readonly string[] Channels = ["LIVE", "PTU", "EPTU"];

    private static readonly string[] SubPaths =
    [
        "StarCitizen",
        Path.Combine("Games", "StarCitizen"),
        Path.Combine("Program Files", "Roberts Space Industries", "StarCitizen"),
        Path.Combine("Roberts Space Industries", "StarCitizen"),
    ];

    public static List<string> FindGameLogPaths(IEnumerable<string>? searchRoots = null)
    {
        var roots = searchRoots ?? GetDriveRoots();
        var found = new List<string>();

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;

            foreach (var sub in SubPaths)
            {
                foreach (var channel in Channels)
                {
                    var logPath = Path.Combine(root, sub, channel, "Game.log");
                    if (File.Exists(logPath))
                        found.Add(logPath);
                }
            }
        }

        // Prefer LIVE > PTU > EPTU, then by most recently written
        found.Sort((a, b) =>
        {
            var ca = ChannelPriority(a);
            var cb = ChannelPriority(b);
            if (ca != cb) return ca.CompareTo(cb);
            try
            {
                return File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a));
            }
            catch { return 0; }
        });

        return found;
    }

    private static int ChannelPriority(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? "";
        var channel = Path.GetFileName(dir);
        return channel.ToUpperInvariant() switch
        {
            "LIVE" => 0,
            "PTU" => 1,
            "EPTU" => 2,
            _ => 3
        };
    }

    public static string? FindGameLogInFolder(string folder)
    {
        if (!Directory.Exists(folder))
            return null;

        // Direct: folder itself contains Game.log (user pointed at e.g. LIVE/)
        var direct = Path.Combine(folder, "Game.log");
        if (File.Exists(direct))
            return Path.GetFullPath(direct);

        // Channel subdirectories: folder is the StarCitizen/ root
        foreach (var channel in Channels)
        {
            var channelLog = Path.Combine(folder, channel, "Game.log");
            if (File.Exists(channelLog))
                return Path.GetFullPath(channelLog);
        }

        return null;
    }

    public static bool IsValidGameLog(string path)
        => File.Exists(path);

    private static IEnumerable<string> GetDriveRoots()
        => DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => d.RootDirectory.FullName);
}
