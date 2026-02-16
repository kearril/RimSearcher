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
        "Regex pattern search across all C# and XML files. Use for finding specific XML tags (e.g., '<thingClass>Apparel</thingClass>'), method signatures, or data patterns. Returns file paths with matching line previews. Limit: 50 results.";

    public string? Icon => "lucide:search-code";

    public object JsonSchema => new
    {
        type = "object",
        properties = new
        {
            pattern = new
            {
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

        try
        {
            var (results, truncated) = await _indexer.SearchRegexAsync(pattern, ignoreCase, cancellationToken, progress);
            if (results.Count == 0) return new ToolResult($"No matches for pattern: {pattern}");

            var output = $"Regex matches for '{pattern}':\n\n" + 
                         string.Join("\n\n", results.Select(r => $"`{r.Path}`\n{r.Preview}"));
            if (truncated)
            {
                output += "\n\n[Results truncated to 50 matches]";
            }

            return new ToolResult(output);
        }
        catch (ArgumentException ex)
        {
            return new ToolResult($"Invalid Regex Pattern: {ex.Message}", true);
        }
    }
}
