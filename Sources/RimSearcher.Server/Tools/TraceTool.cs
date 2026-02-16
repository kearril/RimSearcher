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
        "Cross-reference analysis for C# and XML. Mode 'inheritors': finds all subclasses of a base type (e.g., all HediffComp variants). Mode 'usages': finds all files referencing a symbol with line numbers and code previews. Useful for impact analysis and hook discovery.";

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
            if (inheritors.Count == 0) return new ToolResult($"No subclasses of '{symbol}' found.");

            var results = inheritors.Select(name =>
            {
                var paths = _sourceIndexer.GetPathsByType(name);
                return $"- `{name}` ({string.Join(", ", paths.Select(System.IO.Path.GetFileName))})";
            });

            return new ToolResult($"Subclasses of '{symbol}':\n" + string.Join(Environment.NewLine, results));
        }
        else // usages mode
        {
            var results = new ConcurrentBag<(string file, int lineNum, string preview)>();
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
            int truncatedFlag = 0;

            await Parallel.ForEachAsync(files, cancellationToken, async (file, ct) =>
            {
                if (Interlocked.CompareExchange(ref globalCount, 0, 0) >= 50)
                {
                    Interlocked.Exchange(ref truncatedFlag, 1);
                    return;
                }

                try
                {
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream);

                    string? line;
                    int lineNum = 0;
                    while ((line = await reader.ReadLineAsync(ct)) != null)
                    {
                        lineNum++;
                        if (regex.IsMatch(line))
                        {
                            var currentCount = Interlocked.Increment(ref globalCount);
                            if (currentCount <= 50)
                            {
                                var preview = line.Trim();
                                if (preview.Length > 80) preview = preview.Substring(0, 77) + "...";
                                results.Add((file, lineNum, preview));
                            }
                            if (currentCount >= 50)
                            {
                                Interlocked.Exchange(ref truncatedFlag, 1);
                            }
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

            if (results.Count == 0) return new ToolResult($"No references to '{symbol}' found.");

            var sortedResults = results.OrderBy(r => r.file).ThenBy(r => r.lineNum).Take(50);
            var output = $"References to '{symbol}' ({results.Count} found):\n\n" + 
                         string.Join(Environment.NewLine, sortedResults.Select(r => 
                             $"- `{System.IO.Path.GetFileName(r.file)}:{r.lineNum}` - {r.preview}"));
            
            var wasTruncated = Interlocked.CompareExchange(ref truncatedFlag, 0, 0) == 1;
            if (wasTruncated || results.Count > 50)
            {
                output += $"\n\n[Showing first 50 of {results.Count} matches]";
            }

            return new ToolResult(output);
        }
    }
}