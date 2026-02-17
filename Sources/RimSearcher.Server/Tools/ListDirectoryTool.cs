using System.Text.Json;
using RimSearcher.Core;

namespace RimSearcher.Server.Tools;

public class ListDirectoryTool : ITool
{
    public string Name => "rimworld-searcher__list_directory";

    public string Description =>
        "List files and subdirectories for a given path. Directory names are suffixed with '/'. Supports limit paging and reports when more entries are available.";

    public string? Icon => "lucide:folder-tree";

    public object JsonSchema => new
    {
        type = "object",
        properties = new
        {
            path = new
            {
                type = "string",
                description = "Absolute directory path to inspect. Example: '/path/to/RimWorld/Source/Core/Defs'."
            },
            limit = new
            {
                type = "integer",
                description = "Maximum entries to return. If exceeded, output includes a 'more entries available' hint.",
                @default = 100
            }
        },
        required = new[] { "path" }
    };

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken cancellationToken, IProgress<double>? progress = null)
    {
        var path = args.GetProperty("path").GetString();
        int limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 100;

        if (string.IsNullOrEmpty(path)) return new ToolResult("Path cannot be empty.", true);

        cancellationToken.ThrowIfCancellationRequested();

        if (!PathSecurity.IsPathSafe(path)) return new ToolResult("Path outside allowed directories.", true);
        if (!Directory.Exists(path)) return new ToolResult("Directory not found.", true);

        try
        {
            var entries = Directory.EnumerateFileSystemEntries(path)
                .Take(limit + 1)
                .ToList();

            var hasMore = entries.Count > limit;
            var displayedEntries = entries.Take(limit)
                .Select(e => Path.GetFileName(e) + (Directory.Exists(e) ? "/" : ""));

            var result = $"`{path}`\n" + string.Join("\n", displayedEntries);
            if (hasMore)
            {
                result += $"\n... [more entries available, increase limit]";
            }
            return new ToolResult(result);
        }
        catch (Exception ex)
        {
            return new ToolResult($"Failed to list directory: {ex.Message}", true);
        }
    }
}
