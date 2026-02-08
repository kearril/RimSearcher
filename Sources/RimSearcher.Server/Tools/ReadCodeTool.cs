using System.Text.Json;
using RimSearcher.Core;

namespace RimSearcher.Server.Tools;

public class ReadCodeTool : ITool
{
    public string Name => "rimworld-searcher__read_code";

    public string Description =>
        "Precision extraction of RimWorld's C# logic. Highly recommended: provide a `methodName` to isolate and retrieve only the relevant implementation block, bypassing thousands of lines of boilerplate. Use this to understand the actual execution logic of Comps, Workers, and Ticks.";

    public string? Icon => "lucide:file-code";

    public object JsonSchema => new
    {
        type = "object",
        properties = new
        {
            path = new { type = "string", description = "Absolute file path (found via 'locate'). Example: '/path/to/RimWorld/Source/CompShield.cs'" },
            methodName = new
            {
                type = "string",
                description =
                    "The specific method implementation to read. Example: 'CompTick' or 'DoEffect'."
            },
            className = new
            {
                type = "string",
                description = "Optional: The class name to resolve ambiguity if multiple classes in the file have the same method name."
            },
            startLine = new
            {
                type = "integer",
                description = "Optional: Starting line number (0-based). Used only if methodName is not provided."
            },
            lineCount = new
            {
                type = "integer",
                description = "Optional: Number of lines to read. Defaults to 100."
            }
        },
        required = new[] { "path" }
    };

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken cancellationToken, IProgress<double>? progress = null)
    {
        var path = args.GetProperty("path").GetString();
        if (string.IsNullOrEmpty(path)) return new ToolResult("Path cannot be empty.", true);

        if (!PathSecurity.IsPathSafe(path))
            return new ToolResult("Access Denied: Path is outside allowed source directories.", true);
        if (!File.Exists(path)) return new ToolResult("File does not exist.", true);

        cancellationToken.ThrowIfCancellationRequested();
        if (args.TryGetProperty("methodName", out var mProp))
        {
            var methodName = mProp.GetString();
            if (!string.IsNullOrEmpty(methodName))
            {
                var className = args.TryGetProperty("className", out var cProp) ? cProp.GetString() : null;
                var body = await RoslynHelper.GetMethodBodyAsync(path, methodName, className);
                if (string.IsNullOrEmpty(body) || body.Contains("Method not found"))
                {
                    return new ToolResult(
                        $"Method '{methodName}' not found in {path}. Tips: 1. Ensure the name is correct; 2. Use 'inspect' tool on the class to see all available methods.",
                        true);
                }

                return new ToolResult($"# Method: {methodName}\n```csharp\n{body}\n```");
            }
        }

        // Fallback to line-based paginated reading mode.
        int startLine = args.TryGetProperty("startLine", out var sProp) ? sProp.GetInt32() : 0;
        int lineCount = args.TryGetProperty("lineCount", out var lProp) ? lProp.GetInt32() : 100;

        try
        {
            var resultLines = new List<string>();
            int currentLine = 0;

            foreach (var line in File.ReadLines(path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (currentLine >= startLine && currentLine < startLine + lineCount)
                {
                    resultLines.Add($"L{currentLine + 1}: {line}");
                }

                currentLine++;
                if (currentLine >= startLine + lineCount) break;
            }

            if (resultLines.Count == 0)
                return new ToolResult($"Line range {startLine}-{startLine + lineCount} exceeds file length.", true);

            return new ToolResult(string.Join("\n", resultLines));
        }
        catch (Exception ex)
        {
            return new ToolResult($"Read failed: {ex.Message}", true);
        }
    }
}