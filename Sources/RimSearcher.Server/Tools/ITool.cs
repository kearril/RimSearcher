using System.Text.Json;

namespace RimSearcher.Server.Tools;



/// <summary>
/// 定义 MCP 服务器工具的通用接口。
/// </summary>
public interface ITool
{
    /// <summary>
    /// 工具的名称。
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// 工具的功能描述。
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 工具接收参数的 JSON Schema 定义。
    /// </summary>
    object JsonSchema { get; }
    
    /// <summary>
    /// 执行工具的核心逻辑。
    /// </summary>
    /// <param name="arguments">由 MCP 客户端传递的 JSON 参数。</param>
    /// <returns>执行结果的字符串表示。</returns>
    Task<string> ExecuteAsync(JsonElement arguments);
}