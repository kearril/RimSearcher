using System.Text.Json;
using RimSearcher.Core;

namespace RimSearcher.Server.Tools;

public class ReadCodeTool : ITool
{
    public string Name => "read_code";
    public string Description => "读取源码。支持读取整个方法或指定行范围（分页）。";

    public object JsonSchema => new {
        type = "object",
        properties = new {
            path = new { type = "string", description = "文件路径" },
            methodName = new { type = "string", description = "可选：要读取的具体方法名" },
            className = new { type = "string", description = "可选：当方法名有冲突时指定类名" },
            startLine = new { type = "integer", description = "可选：起始行 (从 0 开始)" },
            lineCount = new { type = "integer", description = "可选：读取行数 (默认 100)" }
        },
        required = new[] { "path" }
    };

    public async Task<string> ExecuteAsync(JsonElement args)
    {
        var path = args.GetProperty("path").GetString();
        if (string.IsNullOrEmpty(path)) return "路径不能为空";

        if (!PathSecurity.IsPathSafe(path)) return "访问被拒绝：路径超出允许范围。";
        if (!File.Exists(path)) return "文件不存在";

        // 尝试按方法名检索，如果提供了 methodName 则使用 Roslyn 解析。
        if (args.TryGetProperty("methodName", out var mProp))
        {
            var methodName = mProp.GetString();
            if (!string.IsNullOrEmpty(methodName))
            {
                var className = args.TryGetProperty("className", out var cProp) ? cProp.GetString() : null;
                return await RoslynHelper.GetMethodBodyAsync(path, methodName, className);
            }
        }

        // 回退到基于行号的分页读取模式。
        int startLine = args.TryGetProperty("startLine", out var sProp) ? sProp.GetInt32() : 0;
        int lineCount = args.TryGetProperty("lineCount", out var lProp) ? lProp.GetInt32() : 100;

        try 
        {
            var resultLines = new List<string>();
            int currentLine = 0;
            
            // 使用流式读取，并为每一行添加行号标识。
            foreach (var line in File.ReadLines(path))
            {
                if (currentLine >= startLine && currentLine < startLine + lineCount)
                {
                    resultLines.Add($"L{currentLine + 1}: {line}");
                }
                currentLine++;
                if (currentLine >= startLine + lineCount) break;
            }

            if (resultLines.Count == 0) return $"行范围 {startLine}-{startLine + lineCount} 超出文件长度。";
            
            return string.Join("\n", resultLines);
        }
        catch (Exception ex)
        {
            return $"读取失败: {ex.Message}";
        }
    }
}