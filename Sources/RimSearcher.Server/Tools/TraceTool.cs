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
    public string Description => "Traces code relationships. 1. 'inheritors' mode: Finds all subclasses of a specified type (downstream inheritance); 2. 'usages' mode: Searches for text references of a symbol across C# and XML files (upstream usage). Useful for impact analysis or finding extension points.";

    public object JsonSchema => new
    {
        type = "object",
        properties = new
        {
            symbol = new { 
                type = "string", 
                description = "The Class name, Method name, or any string symbol to trace. Example: 'Pawn' or 'TakeDamage'." 
            },
            mode = new { 
                type = "string", 
                @enum = new[] { "inheritors", "usages" }, 
                description = "'inheritors': Find subclasses of the symbol. 'usages': Find text references of the symbol in the codebase." 
            }
        },
        required = new[] { "symbol", "mode" }
    };

    public async Task<string> ExecuteAsync(JsonElement args)
    {
        var symbol = args.GetProperty("symbol").GetString();
        var mode = args.GetProperty("mode").GetString();

        if (string.IsNullOrEmpty(symbol)) return "Symbol cannot be empty.";

        if (mode == "inheritors")
        {
            var inheritors = _sourceIndexer.GetInheritors(symbol);
            if (inheritors.Count == 0) return $"No subclasses found for {symbol}.";

            var results = inheritors.Select(name => 
            {
                var paths = _sourceIndexer.GetPathsByType(name);
                return $"{name} (in `{string.Join(", ", paths)}`)";
            });

            return string.Join(Environment.NewLine, results);
        }
        else // usages mode: search for text references globally.
        {
            var results = new ConcurrentBag<string>();
            var files = _sourceIndexer.GetAllFiles()
                .Where(f => f.EndsWith(".cs") || f.EndsWith(".xml"))
                .ToList();
            
            var regex = new System.Text.RegularExpressions.Regex($@"\b{System.Text.RegularExpressions.Regex.Escape(symbol)}\b", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

            int globalCount = 0;
            bool truncated = false;

            await Parallel.ForEachAsync(files, async (file, ct) =>
            {
                if (globalCount >= 50) { truncated = true; return; }

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
            });
            
            if (results.Count == 0) return "No references found.";

            var output = string.Join(Environment.NewLine, results.Take(50).Select(r => $"- `{r}`"));
            if (truncated || globalCount >= 50)
            {
                output += "\n\n--- ⚠️ WARNING: Too many results, truncated to first 50. ---";
            }
            return output;
        }
    }
}