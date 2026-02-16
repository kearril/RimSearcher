using System.Text.Json;
using RimSearcher.Core;

namespace RimSearcher.Server.Tools;

public class ListDirectoryTool : ITool
{
    public string Name => "rimworld-searcher__list_directory";

    public string Description =>
        "Lists directory contents. Use to explore RimWorld source folder structure and verify file organization. Directories are suffixed with '/'.";

    public string? Icon => "lucide:folder-tree";

    public object JsonSchema => new
    {
        type = "object",
        properties = new
        {
            path = new
            {
                type = "string",
                description = "The full path of the directory to list. Example: '/path/to/RimWorld/Mods/MyMod/Defs'."
            },
            limit = new
            {
                type = "integer",
                description = "Maximum number of entries to return to prevent overflow.",
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
            var allEntries = Directory.GetFileSystemEntries(path).ToList();
            var displayedEntries = allEntries.Take(limit)
                .Select(e => Path.GetFileName(e) + (Directory.Exists(e) ? "/" : ""));

            var result = $"`{path}`\n" + string.Join("\n", displayedEntries);
            if (allEntries.Count > limit)
            {
                result += $"\n... [{allEntries.Count - limit} more entries]";
            }
            return new ToolResult(result);
        }
        catch (Exception ex)
        {
            return new ToolResult($"Failed to list directory: {ex.Message}", true);
        }
    }
}
