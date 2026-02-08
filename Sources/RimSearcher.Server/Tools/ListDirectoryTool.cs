using System.Text.Json;
using RimSearcher.Core;

namespace RimSearcher.Server.Tools;

public class ListDirectoryTool : ITool
{
    public string Name => "rimworld-searcher__list_directory";

    public string Description =>
        "Low-level file system explorer. Use this to manually navigate mod folder structures, verify contents of `Patches/` or `Assemblies/` directories, and confirm physical file layout. Recommended when you need to understand how a Mod's assets are organized.";

    public string? Icon => "lucide:folder-tree";

    public object JsonSchema => new
    {
        type = "object",
        properties = new
        {
            path = new { 
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

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken cancellationToken, IProgress<double>? progress = null) {
        var path = args.GetProperty("path").GetString();
        int limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 100;

        if (string.IsNullOrEmpty(path)) return new ToolResult("Path cannot be empty.", true);
        
        cancellationToken.ThrowIfCancellationRequested();

        // Path security validation
        if (!PathSecurity.IsPathSafe(path)) return new ToolResult("Access Denied: Path is outside allowed source directories.", true);
        if (!Directory.Exists(path)) return new ToolResult("Directory does not exist.", true);

        try {
            var allEntries = Directory.GetFileSystemEntries(path).ToList();
            var displayedEntries = allEntries.Take(limit)
                .Select(e => Path.GetFileName(e) + (Directory.Exists(e) ? "/" : ""));
            
            var result = string.Join("\n", displayedEntries);
            if (allEntries.Count > limit) {
                result += $"\n... ({allEntries.Count - limit} more entries omitted)";
            }
            return new ToolResult(result);
        } catch (Exception ex) {
            return new ToolResult($"Failed to list directory: {ex.Message}", true);
        }
    }
}
