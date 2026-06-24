using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssetMemory.Core.Detection;

public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string? GameLogPath { get; set; }
    public List<string> ProcessedBackups { get; set; } = [];

    public static AppSettings Load(string path)
    {
        if (!File.Exists(path))
            return new AppSettings();

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts));
    }
}
