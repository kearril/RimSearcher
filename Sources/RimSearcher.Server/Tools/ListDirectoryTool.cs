using System.Text.Json;
using RimSearcher.Core;

namespace RimSearcher.Server.Tools;

public class ListDirectoryTool : ITool
{
    public string Name => "rimworld-searcher__list_directory";
    public string Description => "Explores the file system. Use this to list files and subdirectories within the RimWorld project or Mod folders.";

    public object JsonSchema => new {
        type = "object",
        properties = new {
            path = new { type = "string", description = "The full path of the directory to list." },
            limit = new { type = "integer", description = "Maximum number of entries to return to prevent overflow.", @default = 100 }
        },
        required = new[] { "path" }
    };

    public async Task<string> ExecuteAsync(JsonElement args) {
        var path = args.GetProperty("path").GetString();
        int limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 100;

        if (string.IsNullOrEmpty(path)) return "Path cannot be empty.";
        
        // Path security validation
        if (!PathSecurity.IsPathSafe(path)) return "Access Denied: Path is outside allowed source directories.";
        if (!Directory.Exists(path)) return "Directory does not exist.";

        var allEntries = Directory.GetFileSystemEntries(path).ToList();
        var displayedEntries = allEntries.Take(limit)
            .Select(e => Path.GetFileName(e) + (Directory.Exists(e) ? "/" : ""));
        
        var result = string.Join("\n", displayedEntries);
        if (allEntries.Count > limit) {
            result += $"\n... ({allEntries.Count - limit} more entries omitted)";
        }
        return result;
    }
}