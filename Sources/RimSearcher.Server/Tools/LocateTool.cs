using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using RimSearcher.Core;

namespace RimSearcher.Server.Tools;

public class LocateTool : ITool
{
    private readonly SourceIndexer _sourceIndexer;
    private readonly DefIndexer _defIndexer;

    public LocateTool(SourceIndexer sourceIndexer, DefIndexer defIndexer)
    {
        _sourceIndexer = sourceIndexer;
        _defIndexer = defIndexer;
    }

    public string Name => "locate";
    public string Description => "全域定位资源。按类型名、DefName、文件名或路径进行搜索。";

    public object JsonSchema => new {
        type = "object",
        properties = new {
            query = new { type = "string", description = "关键词 (类型名、defName 或文件名)" }
        },
        required = new[] { "query" }
    };

    public async Task<string> ExecuteAsync(JsonElement args)
    {
        var query = args.GetProperty("query").GetString();
        if (string.IsNullOrEmpty(query)) return "查询不能为空";

        var sb = new StringBuilder();
        sb.AppendLine($"# Search Results for: '{query}'");

        // 1. 类型模糊匹配
        var types = _sourceIndexer.SearchTypes(query);
        if (types.Any())
        {
            sb.AppendLine("\n## C# Types:");
            foreach (var cls in types.Take(10))
            {
                var paths = _sourceIndexer.GetPathsByType(cls);
                sb.AppendLine($"- **{cls}**: {string.Join(", ", paths)}");
            }
            if (types.Count > 10) sb.AppendLine($"*... (还有 {types.Count - 10} 个类型已省略)*");
        }

        // 2. Def 模糊匹配
        var defs = _defIndexer.Search(query);
        if (defs.Any())
        {
            sb.AppendLine("\n## Defs:");
            foreach (var def in defs.Take(10))
            {
                sb.AppendLine($"- **{def.DefName}** ({def.DefType})");
                sb.AppendLine($"  Path: {def.FilePath}");
                if (!string.IsNullOrEmpty(def.Label)) sb.AppendLine($"  Label: {def.Label}");
            }
            if (defs.Count > 10) sb.AppendLine($"*... (还有 {defs.Count - 10} 个 Def 已省略)*");
        }

        // 3. 文件名模糊匹配
        var files = _sourceIndexer.Search(query).Distinct().ToList();
        if (files.Any())
        {
            sb.AppendLine("\n## Files:");
            foreach (var file in files.Take(10))
            {
                sb.AppendLine($"- {file}");
            }
            if (files.Count > 10) sb.AppendLine($"*... (还有 {files.Count - 10} 个文件已省略)*");
        }

        var result = sb.ToString();
        return string.IsNullOrWhiteSpace(result) || result.Split('\n').Length <= 2 ? "未找到任何匹配资源。" : result;
    }
}