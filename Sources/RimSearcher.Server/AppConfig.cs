using System.Text.Json;

namespace RimSearcher.Server;

/// <summary>
/// 存储服务器的全局配置信息，包括 C# 和 XML 源码的扫描路径。
/// </summary>
public class AppConfig
{
    public List<string> CsharpSourcePaths { get; set; } = new();
    public List<string> XmlSourcePaths { get; set; } = new();

    /// <summary>
    /// 从指定的 JSON 文件加载配置。
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
            Console.Error.WriteLine($"[Config] 加载配置文件失败: {ex.Message}");
            return new AppConfig();
        }
    }
}