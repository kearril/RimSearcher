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
        "Regex pattern search across all C# and XML files. Use for finding specific XML tags (e.g., '<thingClass>Apparel</thingClass>'), method signatures, or data patterns. Returns results grouped by file with line numbers and previews. Shows top 3 matches per file. Limit: 50 files. Use 'fileFilter' to restrict to specific file types.";

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
            ignoreCase = new { type = "boolean", description = "Whether to ignore case, defaults to true." },
            fileFilter = new { type = "string", description = "Optional file extension filter. Example: '.cs' for C# only, '.xml' for XML only." }
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

            // Group by file for compact display
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
