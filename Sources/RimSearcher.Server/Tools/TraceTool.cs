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

    public string Name => "trace";
    public string Description => "追踪关联关系。支持寻找子类 (inheritors) 或查找符号引用 (usages)。";

    public object JsonSchema => new
    {
        type = "object",
        properties = new
        {
            symbol = new { type = "string", description = "要追踪的类型名或方法名" },
            mode = new { type = "string", @enum = new[] { "inheritors", "usages" }, description = "追踪模式" }
        },
        required = new[] { "symbol", "mode" }
    };

    public async Task<string> ExecuteAsync(JsonElement args)
    {
        var symbol = args.GetProperty("symbol").GetString();
        var mode = args.GetProperty("mode").GetString();

        if (string.IsNullOrEmpty(symbol)) return "Symbol 不能为空";

        if (mode == "inheritors")
        {
            var inheritors = _sourceIndexer.GetInheritors(symbol);
            if (inheritors.Count == 0) return $"未找到 {symbol} 的子类。";

            var results = inheritors.Select(name => 
            {
                var paths = _sourceIndexer.GetPathsByType(name);
                return $"{name} (in {string.Join(", ", paths)})";
            });

            return string.Join(Environment.NewLine, results);
        }
        else // usages 模式：在全域范围内查找该符号的文本引用。
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
            
            if (results.Count == 0) return "未找到任何引用。";

            var output = string.Join(Environment.NewLine, results.Take(50));
            if (truncated || globalCount >= 50)
            {
                output += "\n\n--- ⚠️ 注意：引用结果较多，已截断显示前 50 条。 ---";
            }
            return output;
        }
    }
}