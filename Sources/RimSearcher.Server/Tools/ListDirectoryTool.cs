using System.Text.Json;
using RimSearcher.Core;

namespace RimSearcher.Server.Tools;

public class ListDirectoryTool : ITool
{
    public string Name => "list_directory";
    public string Description => "列出指定目录下的文件和子文件夹。";

    public object JsonSchema => new {
        type = "object",
        properties = new {
            path = new { type = "string", description = "目录的完整路径" },
            limit = new { type = "integer", description = "最大返回条目数，防止溢出", @default = 100 }
        },
        required = new[] { "path" }
    };

    public async Task<string> ExecuteAsync(JsonElement args) {
        var path = args.GetProperty("path").GetString();
        int limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 100;

        if (string.IsNullOrEmpty(path)) return "路径不能为空";
        
        // 路径安全校验
        if (!PathSecurity.IsPathSafe(path)) return "访问被拒绝：路径超出允许范围。";
        if (!Directory.Exists(path)) return "目录不存在";

        var allEntries = Directory.GetFileSystemEntries(path).ToList();
        var displayedEntries = allEntries.Take(limit)
            .Select(e => Path.GetFileName(e) + (Directory.Exists(e) ? "/" : ""));
        
        var result = string.Join("\n", displayedEntries);
        if (allEntries.Count > limit) {
            result += $"\n... (还有 {allEntries.Count - limit} 个条目已省略)";
        }
        return result;
    }
}