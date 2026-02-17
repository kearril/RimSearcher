using System.Text.Json;

namespace RimSearcher.Server;

public record AppConfig
{
    public List<string> CsharpSourcePaths { get; init; } = new();
    public List<string> XmlSourcePaths { get; init; } = new();
    public bool SkipPathSecurity { get; init; } = false;
    public bool CheckUpdates { get; init; } = true;

    public static (AppConfig Config, string Path, bool IsLoaded) Load()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var config = JsonSerializer.Deserialize<AppConfig>(json, options);
                if (config != null) return (config, path, true);
            }
        }
        catch { }
        return (new AppConfig(), path, false);
    }
}