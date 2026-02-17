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
        "Regex search across indexed C# and XML files. Returns grouped file results with line previews. Shows up to 3 matches per file and up to 50 files in output. Tested: pattern 'class.*:.*ThingComp' with fileFilter '.cs' returns broad inheritance matches.";

    public string? Icon => "lucide:search-code";

    public object JsonSchema => new
    {
        type = "object",
        properties = new
        {
            pattern = new
            {
                type = "string",
                description = "Regex pattern to search. Examples: '<thingClass>Apparel</thingClass>', 'void CompTick\\(\\)', 'class.*:.*ThingComp'."
            },
            ignoreCase = new { type = "boolean", description = "Whether to ignore case, defaults to true." },
            fileFilter = new { type = "string", description = "Optional extension filter such as '.cs' or '.xml'." }
        },
        required = new[] { "pattern" }
    };

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken cancellationToken, IProgress<double>? progress = null)
    {
        var pattern = args.GetProperty("pattern").GetString();
        var ignoreCase = !args.TryGetProperty("ignoreCase", out var ic) || ic.GetBoolean();
        var fileFilter = args.TryGetProperty("fileFilter", out var ff) ? ff.GetString() : null;

        if (string.IsNullOrEmpty(pattern))
            return new ToolResult("Pattern cannot be empty.", true);

        try
        {
            var (results, truncated) = await _indexer.SearchRegexAsync(pattern, ignoreCase, cancellationToken, progress);
            
            if (!string.IsNullOrEmpty(fileFilter))
            {
                results = results.Where(r => r.Path.EndsWith(fileFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            
            if (results.Count == 0) return new ToolResult($"No matches for pattern: {pattern}");

            var grouped = results.GroupBy(r => r.Path).Take(50);
            
            var output = $"Regex matches for '{pattern}' ({results.Count} found):\n\n" + 
                         string.Join("\n\n", grouped.Select(g => 
                         {
                             var fileName = System.IO.Path.GetFileName(g.Key);
                             var groupItems = g.ToList();
                             var matches = groupItems.Take(3).Select(m => "  " + m.Preview);
                             var moreCount = groupItems.Count > 3 ? $"\n  ... +{groupItems.Count - 3} more in this file" : "";
                             return $"`{fileName}`\n{string.Join("\n", matches)}{moreCount}";
                         }));
            
            if (truncated)
            {
                output += "\n\n[Results limited to 50 files, use more specific pattern to narrow down]";
            }

            return new ToolResult(output);
        }
        catch (ArgumentException ex)
        {
            return new ToolResult($"Invalid Regex Pattern: {ex.Message}", true);
        }
    }
}
