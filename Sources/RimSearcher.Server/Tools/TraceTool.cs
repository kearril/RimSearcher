using System.Text.Json;
using System.Collections.Concurrent;
using RimSearcher.Core;

namespace RimSearcher.Server.Tools;

public class TraceTool : ITool
{
    private readonly SourceIndexer _sourceIndexer;

    public TraceTool(SourceIndexer sourceIndexer)
    {
        _sourceIndexer = sourceIndexer;
    }

    public string Name => "rimworld-searcher__trace";

    public string Description =>
        "Performs deep cross-reference analysis between C# and XML. Use `inheritors` mode to find all specialized variants of a base class (e.g., all `HediffComp` types). Use `usages` mode to perform comprehensive impact analysis by finding every file that references a specific symbol. Vital for detecting mod conflicts and identifying hook points.";

    public string? Icon => "lucide:git-branch";

    public object JsonSchema => new
    {
        type = "object",
        properties = new
        {
            symbol = new
            {
                type = "string",
                description = "The class or member name to trace. Example: 'HediffComp' or 'TakeDamage'."
            },
            mode = new
            {
                type = "string",
                @enum = new[] { "inheritors", "usages" },
                description =
                    "'inheritors': Find subclasses of the symbol. 'usages': Find text references of the symbol in the codebase."
            }
        },
        required = new[] { "symbol", "mode" }
    };

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken cancellationToken, IProgress<double>? progress = null)
    {
        var symbol = args.GetProperty("symbol").GetString();
        var mode = args.GetProperty("mode").GetString();

        if (string.IsNullOrEmpty(symbol)) return new ToolResult("Symbol cannot be empty.", true);

        if (mode == "inheritors")
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inheritors = _sourceIndexer.GetInheritors(symbol);
            if (inheritors.Count == 0) return new ToolResult($"No subclasses found for {symbol}.");

            var results = inheritors.Select(name =>
            {
                var paths = _sourceIndexer.GetPathsByType(name);
                return $"{name} (in `{string.Join(", ", paths)}`)";
            });

            return new ToolResult(string.Join(Environment.NewLine, results));
        }
        else // usages mode
        {
            var results = new ConcurrentBag<string>();
            var files = _sourceIndexer.GetAllFiles()
                .Where(f => f.EndsWith(".cs") || f.EndsWith(".xml"))
                .ToList();

            var regex = new System.Text.RegularExpressions.Regex(
                $@"\b{System.Text.RegularExpressions.Regex.Escape(symbol)}\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.Compiled);

            int globalCount = 0;
            int processedCount = 0;
            int totalFiles = files.Count;
            bool truncated = false;

            await Parallel.ForEachAsync(files, cancellationToken, async (file, ct) =>
            {
                if (globalCount >= 50)
                {
                    truncated = true;
                    return;
                }

                try
                {
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream);

                    string? line;
                    while ((line = await reader.ReadLineAsync(ct)) != null)
                    {
                        if (regex.IsMatch(line))
                        {
                            results.Add(file);
                            Interlocked.Increment(ref globalCount);
                            break;
                        }
                    }
                }
                catch { }
                finally
                {
                    var current = Interlocked.Increment(ref processedCount);
                    if (current % 10 == 0 || current == totalFiles)
                    {
                        progress?.Report((double)current / totalFiles);
                    }
                }
            });

            if (results.Count == 0) return new ToolResult("No references found.");

            var output = string.Join(Environment.NewLine, results.Take(50).Select(r => $"- `{r}`"));
            if (truncated || globalCount >= 50)
            {
                output += "\n\n--- WARNING: Too many results, truncated to first 50. ---";
            }

            return new ToolResult(output);
        }
    }
}