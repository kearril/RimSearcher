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
        "Cross-reference analysis for C# and XML. Mode 'inheritors' lists subclasses; mode 'usages' finds references with file/line previews. Tested: usages 'CompShield' found 9 refs across 6 files, inheritors 'ThingComp' returns a broad subclass list.";

    public string? Icon => "lucide:git-branch";

    public object JsonSchema => new
    {
        type = "object",
        properties = new
        {
            symbol = new
            {
                type = "string",
                description = "Class or member to trace. Examples: 'ThingComp', 'CompShield', 'TakeDamage'."
            },
            mode = new
            {
                type = "string",
                @enum = new[] { "inheritors", "usages" },
                description =
                    "Trace mode: 'inheritors' for subclass tree, 'usages' for textual references in C# and XML."
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
        else
        {
            const int maxMatchesPerFile = 3;
            const int maxTotalResults = 50;
            
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
                if (Interlocked.CompareExchange(ref globalCount, 0, 0) >= maxTotalResults)
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
                    int matchesInFile = 0;
                    while ((line = await reader.ReadLineAsync(ct)) != null)
                    {
                        lineNum++;
                        if (regex.IsMatch(line))
                        {
                            var currentCount = Interlocked.Increment(ref globalCount);
                            if (currentCount <= maxTotalResults)
                            {
                                var preview = line.Trim();
                                if (preview.Length > 100) preview = preview[..97] + "...";
                                results.Add((file, lineNum, preview));
                            }
                            matchesInFile++;
                            if (matchesInFile >= maxMatchesPerFile || currentCount >= maxTotalResults)
                            {
                                if (currentCount >= maxTotalResults)
                                    Interlocked.Exchange(ref truncatedFlag, 1);
                                break;
                            }
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

            var grouped = results
                .GroupBy(r => r.file)
                .OrderBy(g => g.Key);

            int totalMatches = results.Count;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"References to '{symbol}' ({totalMatches} found):");
            sb.AppendLine();

            foreach (var group in grouped)
            {
                var fileTag = group.Key.EndsWith(".xml") ? "[XML]" : "[C#]";
                var fileName = System.IO.Path.GetFileName(group.Key);
                sb.AppendLine($"{fileTag} `{fileName}`");
                foreach (var match in group.OrderBy(m => m.lineNum))
                {
                    sb.AppendLine($"  L{match.lineNum}: {match.preview}");
                }
            }

            var wasTruncated = Interlocked.CompareExchange(ref truncatedFlag, 0, 0) == 1;
            if (wasTruncated || totalMatches >= maxTotalResults)
            {
                sb.AppendLine($"\n[Results truncated at {maxTotalResults}, use more specific symbol to narrow down]");
            }

            return new ToolResult(sb.ToString());
        }
    }
}
