using System.Text.Json;

namespace RimSearcher.Server;

/// <summary>
/// Stores global configuration information for the server, including scan paths for C# and XML source.
/// </summary>
public class AppConfig
{
    public List<string> CsharpSourcePaths { get; set; } = new();
    public List<string> XmlSourcePaths { get; set; } = new();
    
    public static (AppConfig Config, string ActualPath, bool Success) Load()
    {
        var envPath = Environment.GetEnvironmentVariable("RIMSEARCHER_CONFIG");
        if (!string.IsNullOrEmpty(envPath))
        {
            var (cfg, success) = LoadFromFile(envPath);
            return (cfg, envPath, success);
        }

        var defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        var (dCfg, dSuccess) = LoadFromFile(defaultPath);
        return (dCfg, defaultPath, dSuccess);
    }

    private static (AppConfig Config, bool Success) LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            return (new AppConfig(), false);
        }

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<AppConfig>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            if (config == null) return (new AppConfig(), false);
            
            config.CsharpSourcePaths = (config.CsharpSourcePaths ?? new()).Distinct().ToList();
            config.XmlSourcePaths = (config.XmlSourcePaths ?? new()).Distinct().ToList();
            
            return (config, true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Config] CRITICAL: JSON syntax error in {path}: {ex.Message}");
            return (new AppConfig(), false);
        }
    }
}