using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using RimSearcher.Core;

namespace RimSearcher.Server.Tools;

public class LocateTool : ITool
{
    private readonly SourceIndexer _sourceIndexer;
    private readonly DefIndexer _defIndexer;

    public LocateTool(SourceIndexer sourceIndexer, DefIndexer defIndexer)
    {
        _sourceIndexer = sourceIndexer;
        _defIndexer = defIndexer;
    }

    public string Name => "rimworld-searcher__locate";

    public string Description =>
        "The mandatory entry point for any RimWorld analysis. Instantly maps a DefName, C# Type, or filename to its physical disk path across Core and all active Mods. Use this tool first whenever the target file path is unknown to ensure subsequent tools have the correct file context.";

    public string? Icon => "lucide:map-pin";

    public object JsonSchema => new
    {
        type = "object",
        properties = new
        {
            query = new
            {
                type = "string",
                description =
                    "The term to find (DefName, Class, or filename). Example: 'Apparel_ShieldBelt' or 'RimWorld.Pawn'."
            }
        },
        required = new[] { "query" }
    };

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken cancellationToken, IProgress<double>? progress = null)
    {
        var query = args.GetProperty("query").GetString();
        if (string.IsNullOrEmpty(query)) return new ToolResult("Query cannot be empty.", true);

        cancellationToken.ThrowIfCancellationRequested();

        var sb = new StringBuilder();
        sb.AppendLine($"# Search Results for: '{query}'");

        // 1. Fuzzy matching for Types
        var types = _sourceIndexer.SearchTypes(query);
        if (types.Any())
        {
            sb.AppendLine("\n## C# Types (Code)");
            foreach (var cls in types.Take(10))
            {
                var paths = _sourceIndexer.GetPathsByType(cls);
                sb.AppendLine($"- **{cls}**\n  - Paths: `{string.Join(", ", paths)}` - Hint: Use 'read_code' with these paths.");
            }
            if (types.Count > 10) sb.AppendLine($"*... ({types.Count - 10} more types omitted)*");
        }

        // 2. Fuzzy matching for Defs
        var defs = _defIndexer.Search(query);
        if (defs.Any())
        {
            sb.AppendLine("\n## XML Defs (Data)");
            foreach (var def in defs.Take(10))
            {
                sb.AppendLine($"- **{def.DefName}** ({def.DefType})");
                sb.AppendLine($"  - Path: `{def.FilePath}`");
                if (!string.IsNullOrEmpty(def.Label)) sb.AppendLine($"  - Label: {def.Label}");
                sb.AppendLine("  - Action: Use 'inspect' with this DefName for full resolved XML.");
            }
            if (defs.Count > 10) sb.AppendLine($"*... ({defs.Count - 10} more Defs omitted)*");
        }

        // 3. Fuzzy matching for filenames
        var files = _sourceIndexer.Search(query).Distinct().ToList();
        if (files.Any())
        {
            sb.AppendLine("\n## Matching Files");
            foreach (var file in files.Take(10))
            {
                sb.AppendLine($"- `{file}`");
            }
            if (files.Count > 10) sb.AppendLine($"*... ({files.Count - 10} more files omitted)*");
        }

        var result = sb.ToString();
        if (string.IsNullOrWhiteSpace(result) || result.Split('\n').Length <= 2)
        {
            return new ToolResult($"No resources found matching '{query}'.\nTips:\n1. Try a partial name.\n2. Use 'search_regex' for content inside files.", true);
        }
        return new ToolResult(result);
    }
}