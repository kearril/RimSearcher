using System.Text;
using System.Text.Json;
using RimSearcher.Core;

namespace RimSearcher.Server.Tools;

public class ReadCodeTool : ITool
{
    private readonly SourceIndexer _sourceIndexer;

    public ReadCodeTool(SourceIndexer sourceIndexer)
    {
        _sourceIndexer = sourceIndexer;
    }

    public string Name => "rimworld-searcher__read_code";

    public string Description =>
        "Extracts C# source code from files. Provide 'methodName' to extract a specific member (method, property, constructor, indexer, operator). Use 'extractClass' to extract an entire class body. Or use 'startLine' and 'lineCount' for raw line-based reading (defaults to 150 lines). Essential for understanding implementation details of Comps, Workers, and game logic.";

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
                    "The member to extract: method ('CompTick'), property ('Label'), constructor (class name or '.ctor'), indexer ('this'), or operator ('+')."
            },
            className = new
            {
                type = "string",
                description = "Optional: The class name to resolve ambiguity if multiple classes have the same member name."
            },
            extractClass = new
            {
                type = "string",
                description = "Optional: Extract the entire class/struct/interface body by name. Example: 'CompShield'."
            },
            startLine = new
            {
                type = "integer",
                description = "Optional: Starting line number (0-based). Used only if methodName is not provided."
            },
            lineCount = new
            {
                type = "integer",
                description = "Optional: Number of lines to read. Defaults to 150."
            }
        },
        required = new[] { "path" }
    };

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken cancellationToken, IProgress<double>? progress = null)
    {
        var path = args.GetProperty("path").GetString();
        if (string.IsNullOrEmpty(path)) return new ToolResult("Path cannot be empty.", true);

        // Auto-resolve: if path is not absolute or file doesn't exist, try to resolve via index
        var resolvedPath = ResolvePath(path);
        if (resolvedPath == null)
            return new ToolResult($"File not found: '{Path.GetFileName(path)}'. Use 'locate' to find the correct file first.", true);

        path = resolvedPath;
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Extract entire class body
            if (args.TryGetProperty("extractClass", out var ecProp))
            {
                var extractClassName = ecProp.GetString();
                if (!string.IsNullOrEmpty(extractClassName))
                {
                    var classBody = await RoslynHelper.GetClassBodyAsync(path, extractClassName);
                    if (string.IsNullOrEmpty(classBody) || classBody.Contains("not found"))
                        return new ToolResult($"Class '{extractClassName}' not found in {Path.GetFileName(path)}. Use inspect tool to verify.", true);
                    return new ToolResult($"```csharp\n{classBody}\n```");
                }
            }

            // Extract specific member (method, property, constructor, etc.)
            if (args.TryGetProperty("methodName", out var mProp))
            {
                var methodName = mProp.GetString();
                if (!string.IsNullOrEmpty(methodName))
                {
                    var className = args.TryGetProperty("className", out var cProp) ? cProp.GetString() : null;
                    var body = await RoslynHelper.GetMemberBodyAsync(path, methodName, className);
                    if (string.IsNullOrEmpty(body) || body.Contains("not found"))
                    {
                        return new ToolResult(
                            $"Member '{methodName}' not found in {Path.GetFileName(path)}. Use inspect tool to see available members.",
                            true);
                    }

                    return new ToolResult($"```csharp\n// {methodName}\n{body}\n```");
                }
            }

            // Fall back to raw line-based reading
            int startLine = args.TryGetProperty("startLine", out var sProp) ? sProp.GetInt32() : 0;
            int lineCount = args.TryGetProperty("lineCount", out var lProp) ? lProp.GetInt32() : 150;

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

    private string? ResolvePath(string input)
    {
        // 1. Absolute path that exists and is safe
        if (Path.IsPathRooted(input) && File.Exists(input) && PathSecurity.IsPathSafe(input))
            return input;

        // 2. Index key is FileNameWithoutExtension — try that first
        var nameNoExt = Path.GetFileNameWithoutExtension(input);
        var indexPath = _sourceIndexer.GetPath(nameNoExt);
        if (indexPath != null && File.Exists(indexPath))
            return indexPath;

        // 3. Also try the raw file name (in case index was built differently)
        var rawName = Path.GetFileName(input);
        if (rawName != nameNoExt)
        {
            indexPath = _sourceIndexer.GetPath(rawName);
            if (indexPath != null && File.Exists(indexPath))
                return indexPath;
        }

        return null;
    }
}