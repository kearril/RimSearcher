using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using RimSearcher.Core;

namespace RimSearcher.Server.Tools;

public class InspectTool : ITool
{
    private readonly SourceIndexer _sourceIndexer;
    private readonly DefIndexer _defIndexer;

    public InspectTool(SourceIndexer sourceIndexer, DefIndexer defIndexer)
    {
        _sourceIndexer = sourceIndexer;
        _defIndexer = defIndexer;
    }

    public string Name => "rimworld-searcher__inspect";

    public string Description =>
        "Analyzes RimWorld defs and C# types. Resolves XML ParentName inheritance chains and exposes merged values. Extracts linked C# Worker/Comp classes from defs (shows top 10). Shows C# class outlines with inheritance graphs.";

    public string? Icon => "lucide:eye";

    public object JsonSchema => new
    {
        type = "object",
        properties = new
        {
            name = new
            {
                type = "string",
                description = "The exact DefName or Class name to inspect. Example: 'Apparel_ShieldBelt' or 'RimWorld.Pawn'."
            }
        },
        required = new[] { "name" }
    };

    public async Task<ToolResult> ExecuteAsync(JsonElement args, CancellationToken cancellationToken, IProgress<double>? progress = null)
    {
        var name = args.GetProperty("name").GetString();
        if (string.IsNullOrEmpty(name)) return new ToolResult("Name cannot be empty.", true);

        cancellationToken.ThrowIfCancellationRequested();

        var sb = new StringBuilder();

        var def = _defIndexer.GetDef(name);
        if (def != null)
        {
            sb.AppendLine($"## Def: {name}");
            sb.AppendLine($"Type: {def.DefType}");

            var typePaths = _sourceIndexer.GetPathsByType(def.DefType);
            if (typePaths.Count > 0)
                sb.AppendLine($"C# Class: `{def.DefType}` ({string.Join(", ", typePaths.Select(Path.GetFileName))})");

            sb.AppendLine($"File: `{def.FilePath}`");

            var resolvedXmlStr = await XmlInheritanceHelper.ResolveDefXmlAsync(name, _defIndexer);
            sb.AppendLine("\n**Resolved XML:**");

            var xmlLines = resolvedXmlStr.Split('\n');
            if (xmlLines.Length > 300)
            {
                sb.AppendLine("```xml");
                for (int i = 0; i < 200; i++) sb.AppendLine(xmlLines[i]);
                sb.AppendLine($"\n... [Truncated {xmlLines.Length - 250} lines] ...\n");
                for (int i = xmlLines.Length - 50; i < xmlLines.Length; i++) sb.AppendLine(xmlLines[i]);
                sb.AppendLine("```");
                sb.AppendLine($"(Full XML: {xmlLines.Length} lines, use read_code on file path above)");
            }
            else
            {
                sb.AppendLine("```xml");
                sb.AppendLine(resolvedXmlStr);
                sb.AppendLine("```");
            }

            try
            {
                var xdoc = XDocument.Parse(resolvedXmlStr);
                var classTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "thingClass", "workerClass", "jobClass", "hediffClass", "thoughtClass",
                    "compClass", "incidentClass", "interactionWorkerClass", "mentalStateHandlerClass",
                    "ritualBehaviorClass", "skillGiverClass", "worldObjectClass", "lifeStageWorkerClass",
                    "traitWorkerClass", "selectionWorkerClass", "modExtension", "giverClass",
                    "soundClass", "damageWorkerClass", "linkDrawerClass", "graphicClass",
                    "blueprintClass", "scattererClass", "questClass", "verbClass",
                    "roomRoleWorker", "statWorker", "moteClass", "thinkTree",
                    "driverClass", "lordJob", "tabWindowClass", "pageClass", "comparerClass",
                    "drawStyleType", "fleckSystemClass", "subEffecterClass", "needClass",
                    "taleClass", "triggerClass", "inheritanceWorkerOverrideClass", "workerType",
                    "eventClass", "worldDrawLayer", "designatorType", "scenPartClass", "stateClass"
                };

                var foundTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var el in xdoc.Descendants())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (classTags.Contains(el.Name.LocalName) ||
                        el.Name.LocalName.EndsWith("Class", StringComparison.OrdinalIgnoreCase) ||
                        el.Name.LocalName.EndsWith("Worker", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = el.Value.Trim();
                        if (!string.IsNullOrEmpty(val)) foundTypes.Add(val);
                    }

                    var classAttr = el.Attribute("Class");
                    if (classAttr != null)
                    {
                        var val = classAttr.Value.Trim();
                        if (!string.IsNullOrEmpty(val)) foundTypes.Add(val);
                    }
                }

                if (foundTypes.Count > 0)
                {
                    sb.AppendLine("\n**Linked C# Types:**");
                    var typesArray = foundTypes.Take(10).ToArray();
                    foreach (var cls in typesArray)
                    {
                        var paths = _sourceIndexer.GetPathsByType(cls);
                        if (paths.Count > 0)
                            sb.AppendLine($"- `{cls}` ({string.Join(", ", paths.Select(Path.GetFileName))})");
                        else
                            sb.AppendLine($"- `{cls}` (not indexed)");
                    }
                    if (foundTypes.Count > 10)
                        sb.AppendLine($"  ... +{foundTypes.Count - 10} more types (use locate to find them)");
                }
            }
            catch
            {
            }

            return new ToolResult(sb.ToString());
        }

        var csharpPaths = _sourceIndexer.GetPathsByType(name);
        if (csharpPaths.Count > 0)
        {
            sb.AppendLine($"## C# Type: {name}");

            var chain = _sourceIndexer.GetInheritanceChain(name);
            if (chain.Count > 0)
            {
                sb.AppendLine("\n**Inheritance:**");
                sb.AppendLine("```mermaid\ngraph TD");
                foreach (var (child, parent) in chain) sb.AppendLine($"    {child} --> {parent}");
                sb.AppendLine("```\n");
            }

            foreach (var path in csharpPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.AppendLine($"**Outline** (`{path}`):");
                sb.AppendLine(await RoslynHelper.GetClassOutlineAsync(path, name));
                sb.AppendLine("---");
            }

            return new ToolResult(sb.ToString());
        }

        return new ToolResult(
            $"'{name}' not found. Use locate tool first to find exact names (case-sensitive).",
            true);
    }
}
