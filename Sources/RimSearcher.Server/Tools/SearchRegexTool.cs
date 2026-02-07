using System.Text.Json;
using RimSearcher.Core;

namespace RimSearcher.Server.Tools;

public class SearchRegexTool : ITool
{
    private readonly SourceIndexer _indexer;

    public SearchRegexTool(SourceIndexer indexer)
    {
        _indexer = indexer;
    }

    public string Name => "search_regex";
    public string Description => "在整个源码库（C# 和 XML）中使用正则表达式进行内容搜索。";

    public object JsonSchema => new {
        type = "object",
        properties = new {
            pattern = new { type = "string", description = "正则表达式模式" },
            ignoreCase = new { type = "boolean", description = "是否忽略大小写，默认为 true" }
        },
        required = new[] { "pattern" }
    };

    public async Task<string> ExecuteAsync(JsonElement args)
    {
        var pattern = args.GetProperty("pattern").GetString();
        var ignoreCase = !args.TryGetProperty("ignoreCase", out var ic) || ic.GetBoolean();

        if (string.IsNullOrEmpty(pattern)) return "模式不能为空";

        var (results, truncated) = await _indexer.SearchRegexAsync(pattern, ignoreCase);
        if (results.Count == 0) return "未找到匹配内容";

        var output = string.Join("\n\n", results.Select(r => $"File: {r.Path}\nMatch: {r.Preview}"));
        if (truncated)
        {
            output += "\n\n--- ⚠️ 注意：匹配结果超过上限，已截断。建议使用更精确的正则表达式。 ---";
        }
        return output;
    }
}