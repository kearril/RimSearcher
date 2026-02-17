using System.Text.Json;

namespace RimSearcher.Server;

public record AppConfig
{
    public const string ConfigPathEnvVar = "RIMSEARCHER_CONFIG";

    public List<string> CsharpSourcePaths { get; init; } = new();
    public List<string> XmlSourcePaths { get; init; } = new();
    public bool SkipPathSecurity { get; init; } = false;
    public bool CheckUpdates { get; init; } = true;

    public static (AppConfig Config, string Path, bool IsLoaded) Load()
    {
        var envPath = Environment.GetEnvironmentVariable(ConfigPathEnvVar);
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            var resolvedEnvPath = ResolvePath(envPath);
            if (TryLoad(resolvedEnvPath, out var configFromEnv))
                return (configFromEnv, resolvedEnvPath, true);

            return (new AppConfig(), resolvedEnvPath, false);
        }

        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        if (TryLoad(path, out var config))
            return (config, path, true);

        return (new AppConfig(), path, false);
    }

    private static bool TryLoad(string path, out AppConfig config)
    {
        config = new AppConfig();
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var loaded = JsonSerializer.Deserialize<AppConfig>(json, options);
                if (loaded != null)
                {
                    config = loaded;
                    return true;
                }
            }
        }
        catch { }

        return false;
    }

    private static string ResolvePath(string rawPath)
    {
        var expanded = Environment.ExpandEnvironmentVariables(rawPath.Trim().Trim('"'));
        if (Path.IsPathRooted(expanded))
            return Path.GetFullPath(expanded);

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        return Path.GetFullPath(Path.Combine(baseDir, expanded));
    }
}
