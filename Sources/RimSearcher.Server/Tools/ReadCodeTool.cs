using System.Text;
using System.Text.Json;
using RimSearcher.Core;

namespace RimSearcher.Server.Tools;

public class ReadCodeTool : ITool
{
    public string Name => "rimworld-searcher__read_code";

    public string Description =>
        "Extracts C# source code from files. Provide 'methodName' to extract a specific method body (recommended). Or use 'startLine' and 'lineCount' for raw line-based reading. Essential for understanding implementation details of Comps, Workers, and game logic.";

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
                description = "Optional: Number of lines to read. Defaults to 300."
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
                        $"Method '{methodName}' not found in {Path.GetFileName(path)}. Use inspect tool to see available methods.",
                        true);
                }

                return new ToolResult($"```csharp\n// {methodName}\n{body}\n```");
            }
        }

        int startLine = args.TryGetProperty("startLine", out var sProp) ? sProp.GetInt32() : 0;
        int lineCount = args.TryGetProperty("lineCount", out var lProp) ? lProp.GetInt32() : 300;

        try
        {
            var allLines = File.ReadAllLines(path);
            int totalLines = allLines.Length;

            var resultLines = allLines.Skip(startLine).Take(lineCount).Select((line, idx) => $"L{startLine + idx + 1}: {line}").ToList();

            if (resultLines.Count == 0)
                return new ToolResult($"Line range {startLine + 1}-{startLine + lineCount} exceeds file length ({totalLines} lines).", true);

            var sb = new StringBuilder();
            sb.AppendLine($"```csharp");
            sb.AppendLine($"// {Path.GetFileName(path)} (lines {startLine + 1}-{Math.Min(startLine + lineCount, totalLines)} of {totalLines})");
            foreach (var line in resultLines) sb.AppendLine(line);
            sb.AppendLine("```");

            if (startLine + lineCount < totalLines)
            {
                sb.AppendLine($"\n[{totalLines - (startLine + lineCount)} more lines available, use startLine={startLine + lineCount}]");
            }

            return new ToolResult(sb.ToString());
        }
        catch (Exception ex)
        {
            return new ToolResult($"Read failed: {ex.Message}", true);
        }
    }
}