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

    public string Description =>
        "High-performance deep scan for hidden patterns. Use complex regex to find specific XML tag values (e.g., `<texValue>.*100</texValue>`) or unique code signatures across the entire project. Best used when searching for specific data properties or obfuscated logic fragments that indexed searches might miss.";

    public string? Icon => "lucide:search-code";

    public object JsonSchema => new
    {
        type = "object",
        properties = new
        {
            pattern = new { 
                type = "string", 
                description = "The regex pattern. Example: '<thingClass>Apparel</thingClass>' or 'void CompTick\\(\\)'." 
            },
            ignoreCase = new { type = "boolean", description = "Whether to ignore case, defaults to true." }
        },
        required = new[] { "pattern" }
    };

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken cancellationToken, IProgress<double>? progress = null)
    {
        var pattern = args.GetProperty("pattern").GetString();
        var ignoreCase = !args.TryGetProperty("ignoreCase", out var ic) || ic.GetBoolean();

        if (string.IsNullOrEmpty(pattern)) 
            return new ToolResult("Pattern cannot be empty.", true);

        await ServerLogger.Info($"Running regex search for pattern: '{pattern}' (ignoreCase: {ignoreCase})");

        try
        {
            var (results, truncated) = await _indexer.SearchRegexAsync(pattern, ignoreCase, cancellationToken, progress);
            if (results.Count == 0) return new ToolResult("No matches found.");

            var output = string.Join("\n\n", results.Select(r => $"File: `{r.Path}`\nMatch: {r.Preview}"));
            if (truncated)
            {
                output += "\n\n--- WARNING: Search results exceeded limit and were truncated. ---";
            }

            return new ToolResult(output);
        }
        catch (ArgumentException ex)
        {
            return new ToolResult($"Invalid Regex Pattern: {ex.Message}", true);
        }
    }
}
