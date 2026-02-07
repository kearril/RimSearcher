using System.Text.Json;

namespace RimSearcher.Server;

/// <summary>
/// Stores global configuration information for the server, including scan paths for C# and XML source.
/// </summary>
public class AppConfig
{
    public List<string> CsharpSourcePaths { get; set; } = new();
    public List<string> XmlSourcePaths { get; set; } = new();

    /// <summary>
    /// Loads configuration from a specified JSON file.
    /// </summary>
    public static AppConfig Load(string path)
    {
        if (!File.Exists(path)) return new AppConfig();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AppConfig();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Config] Failed to load config file: {ex.Message}");
            return new AppConfig();
        }
    }
}