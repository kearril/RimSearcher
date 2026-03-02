using System.Text.Json;
using RimSearcher.Core;

namespace RimSearcher.Server.Tools;

public class ListDirectoryTool : ITool
{
    public string Name => "rimworld-searcher__list_directory";

    public string Description =>
        "List files/subdirectories under an absolute allowed path. Directory names are suffixed with '/'.";

    public object JsonSchema => new
    {
        type = "object",
        properties = new
        {
            path = new
            {
                type = "string",
                minLength = 1,
                description = "Absolute directory path to inspect. Example: '/path/to/RimWorld/Source/Core/Defs'."
            },
            limit = new
            {
                type = "integer",
                minimum = 1,
                maximum = 1000,
                description = "Maximum entries to return. If exceeded, output includes a 'more entries available' hint.",
                @default = 100
            }
        },
        required = new[] { "path" },
        additionalProperties = false
    };

    public Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken cancellationToken, IProgress<double>? progress = null)
    {
        var path = args.GetProperty("path").GetString();
        int limit = args.TryGetProperty("limit", out var l) ? l.GetInt32() : 100;

        if (string.IsNullOrEmpty(path)) return Task.FromResult(new ToolResult("Path cannot be empty.", true));

        cancellationToken.ThrowIfCancellationRequested();

        if (!PathSecurity.IsPathSafe(path)) return Task.FromResult(new ToolResult("Path outside allowed directories.", true));
        if (!Directory.Exists(path)) return Task.FromResult(new ToolResult("Directory not found.", true));

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
            return Task.FromResult(new ToolResult(result));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ToolResult($"Failed to list directory: {ex.Message}", true));
        }
    }
}
