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
        "Fuzzy search for RimWorld C# classes, methods, fields, and XML defs. Supports typo tolerance, CamelCase shortcuts (e.g., 'JDW' → 'JobDriver_Wait'), and query filters (type:, method:, field:, def:). Primary tool for locating code elements.";

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
        var rawQuery = args.GetProperty("query").GetString();
        if (string.IsNullOrEmpty(rawQuery)) return new ToolResult("Query cannot be empty.", true);

        cancellationToken.ThrowIfCancellationRequested();

        var query = QueryParser.Parse(rawQuery);

        var sb = new StringBuilder();
        sb.AppendLine($"## '{rawQuery}'");

        if (query.TypeFilter != null || (string.IsNullOrEmpty(query.MethodFilter) && string.IsNullOrEmpty(query.FieldFilter) && string.IsNullOrEmpty(query.DefFilter)))
        {
            var typeSearchTerm = query.TypeFilter ?? QueryParser.GetCombinedSearchTerm(query);
            var types = _sourceIndexer.FuzzySearchTypes(typeSearchTerm);

            if (types.Any())
            {
                sb.AppendLine("\n**C# Types:**");
                foreach (var (typeName, score) in types.Take(10))
                {
                    var paths = _sourceIndexer.GetPathsByType(typeName);
                    var firstPath = paths.FirstOrDefault() ?? "unknown";
                    var fileName = Path.GetFileName(firstPath);
                    sb.AppendLine($"- `{typeName}` ({score:F0}%) - {fileName}");
                }
                if (types.Count > 10)
                    sb.AppendLine($"  ... +{types.Count - 10} more");
            }
        }

        if (query.MethodFilter != null || query.FieldFilter != null || query.Keywords.Any())
        {
            var keywords = new List<string>();
            if (query.MethodFilter != null) keywords.Add(query.MethodFilter);
            if (query.FieldFilter != null) keywords.Add(query.FieldFilter);
            keywords.AddRange(query.Keywords);

            var members = _sourceIndexer.SearchMembersByKeywords(keywords.ToArray());

            if (members.Any())
            {
                sb.AppendLine("\n**Members:**");

                var groupedMembers = members.GroupBy(m => m.MemberType).ToList();

                foreach (var group in groupedMembers)
                {
                    sb.AppendLine($"  {group.Key}s:");
                    foreach (var (typeName, memberName, memberType, filePath, score) in group.Take(5))
                    {
                        sb.AppendLine($"  - `{typeName}.{memberName}` ({score:F0}%) - {Path.GetFileName(filePath)}");
                    }
                    if (group.Count() > 5)
                        sb.AppendLine($"    ... +{group.Count() - 5} more");
                }
            }
        }

        if (query.DefFilter != null || (string.IsNullOrEmpty(query.TypeFilter) && string.IsNullOrEmpty(query.MethodFilter) && string.IsNullOrEmpty(query.FieldFilter)))
        {
            var defSearchTerm = query.DefFilter ?? QueryParser.GetCombinedSearchTerm(query);
            var defs = _defIndexer.FuzzySearch(defSearchTerm);

            if (defs.Any())
            {
                sb.AppendLine("\n**XML Defs:**");
                foreach (var (def, score) in defs.Take(10))
                {
                    var abstractTag = def.IsAbstract ? " [Abstract]" : "";
                    var label = !string.IsNullOrEmpty(def.Label) ? $" \"{def.Label}\"" : "";
                    sb.AppendLine($"- `{def.DefName}` ({score:F0}%) - {def.DefType}{abstractTag}{label}");
                }
                if (defs.Count > 10)
                    sb.AppendLine($"  ... +{defs.Count - 10} more");
            }

            if (query.Keywords.Any())
            {
                var defsByContent = _defIndexer.SearchByContent(query.Keywords.ToArray());

                if (defsByContent.Any())
                {
                    sb.AppendLine("\n**Content Matches:**");

                    foreach (var (location, matchedFields) in defsByContent.Take(10))
                    {
                        var fieldSummary = string.Join(", ", matchedFields.Take(3));
                        var moreFields = matchedFields.Count > 3 ? $" +{matchedFields.Count - 3}" : "";
                        sb.AppendLine($"- `{location.DefName}` - {fieldSummary}{moreFields}");
                    }
                    if (defsByContent.Count > 10)
                        sb.AppendLine($"  ... +{defsByContent.Count - 10} more");
                }
            }
        }

        var files = _sourceIndexer.Search(rawQuery).Distinct().ToList();
        if (files.Any() && files.Count <= 10)
        {
            sb.AppendLine("\n**Files:**");
            foreach (var file in files.Take(10))
            {
                sb.AppendLine($"- {Path.GetFileName(file)} - {file}");
            }
            if (files.Count > 10)
                sb.AppendLine($"  ... +{files.Count - 10} more");
        }

        var result = sb.ToString();

        var resultLines = result.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (resultLines.Count <= 1)
        {
            return new ToolResult(
                $"No results for '{rawQuery}'.\n\n" +
                "Try: partial names, query filters (type:, method:, field:, def:), or search_regex for patterns.",
                true);
        }

        return new ToolResult(result);
    }
}