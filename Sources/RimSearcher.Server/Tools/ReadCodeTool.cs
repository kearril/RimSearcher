using System.Text.Json;
using RimSearcher.Core;

namespace RimSearcher.Server.Tools;

public class ReadCodeTool : ITool
{
    public string Name => "rimworld-searcher__read_code";
    public string Description => "Smartly reads source code. 1. (Recommended) Extract the complete implementation of a method by providing 'methodName'; 2. Alternatively, read by line range (pagination). Prefer this tool when you need to understand the logic of a specific class member.";

    public object JsonSchema => new {
        type = "object",
        properties = new {
            path = new { type = "string", description = "Absolute file path." },
            methodName = new { 
                type = "string", 
                description = "(Highly Recommended) The name of the method to extract. Using this avoids manual line counting. Example: 'DoEffect' or 'Tick'." 
            },
            className = new { 
                type = "string", 
                description = "Optional: The class name to resolve ambiguity if multiple classes in the file have the same method name." 
            },
            startLine = new { 
                type = "integer", 
                description = "Optional: Starting line number (0-based). Used only if methodName is not provided." 
            },
            lineCount = new { 
                type = "integer", 
                description = "Optional: Number of lines to read. Defaults to 100." 
            }
        },
        required = new[] { "path" }
    };

    public async Task<string> ExecuteAsync(JsonElement args)
    {
        var path = args.GetProperty("path").GetString();
        if (string.IsNullOrEmpty(path)) return "Path cannot be empty.";

        if (!PathSecurity.IsPathSafe(path)) return "Access Denied: Path is outside allowed source directories.";
        if (!File.Exists(path)) return "File does not exist.";

        // Try to retrieve by method name; use Roslyn for parsing if methodName is provided.
        if (args.TryGetProperty("methodName", out var mProp))
        {
            var methodName = mProp.GetString();
            if (!string.IsNullOrEmpty(methodName))
            {
                var className = args.TryGetProperty("className", out var cProp) ? cProp.GetString() : null;
                var body = await RoslynHelper.GetMethodBodyAsync(path, methodName, className);
                if (string.IsNullOrEmpty(body) || body.Contains("Method not found"))
                {
                    return $"Method '{methodName}' not found in {path}. Tips: 1. Ensure the method name is correct (case-sensitive); 2. Use 'inspect' tool on the class to see all available methods.";
                }
                return $"# Method: {methodName}\n```csharp\n{body}\n```";
            }
        }

        // Fallback to line-based paginated reading mode.
        int startLine = args.TryGetProperty("startLine", out var sProp) ? sProp.GetInt32() : 0;
        int lineCount = args.TryGetProperty("lineCount", out var lProp) ? lProp.GetInt32() : 100;

        try 
        {
            var resultLines = new List<string>();
            int currentLine = 0;
            
            // Use streaming read and add line numbers for identification.
            foreach (var line in File.ReadLines(path))
            {
                if (currentLine >= startLine && currentLine < startLine + lineCount)
                {
                    resultLines.Add($"L{currentLine + 1}: {line}");
                }
                currentLine++;
                if (currentLine >= startLine + lineCount) break;
            }

            if (resultLines.Count == 0) return $"Line range {startLine}-{startLine + lineCount} exceeds file length.";
            
            return string.Join("\n", resultLines);
        }
        catch (Exception ex)
        {
            return $"Read failed: {ex.Message}";
        }
    }
}