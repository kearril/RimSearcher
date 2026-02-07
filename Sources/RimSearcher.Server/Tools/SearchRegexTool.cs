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

    public string Name => "rimworld-searcher__search_regex";
    public string Description => "Global content search. Performs advanced regex-based searching across the entire codebase (C# and XML). Use this when looking for specific string constants, complex code patterns, or XML tag combinations.";

    public object JsonSchema => new {
        type = "object",
        properties = new {
            pattern = new { type = "string", description = "The regex pattern to search for." },
            ignoreCase = new { type = "boolean", description = "Whether to ignore case, defaults to true." }
        },
        required = new[] { "pattern" }
    };

    public async Task<string> ExecuteAsync(JsonElement args)
    {
        var pattern = args.GetProperty("pattern").GetString();
        var ignoreCase = !args.TryGetProperty("ignoreCase", out var ic) || ic.GetBoolean();

        if (string.IsNullOrEmpty(pattern)) return "Pattern cannot be empty.";

        var (results, truncated) = await _indexer.SearchRegexAsync(pattern, ignoreCase);
        if (results.Count == 0) return "No matches found.";

        var output = string.Join("\n\n", results.Select(r => $"File: `{r.Path}`\nMatch: {r.Preview}"));
        if (truncated)
        {
            output += "\n\n--- ⚠️ WARNING: Search results exceeded limit and were truncated. Use a more specific regex. ---";
        }
        return output;
    }
}