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
        "The ultimate analyzer for RimWorld's complex data-code links. Crucial for XML analysis as it automatically resolves `ParentName` inheritance—a process the AI model's internal knowledge cannot accurately perform. It exposes final merged values and identifies the exact C# Worker/Comp classes bound to a Def. Also provides C# class outlines and inheritance graphs.";

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

        //  Try to resolve as a Def first.
        var def = _defIndexer.GetDef(name);
        if (def != null)
        {
            sb.AppendLine($"# Def Analysis: {name}");
            sb.AppendLine($"- **Type**: {def.DefType}");
            
            //  Semantic Bridge (Def Tag -> C# Class)
            var typePaths = _sourceIndexer.GetPathsByType(def.DefType);
            if (typePaths.Count > 0)
                sb.AppendLine($"- **Handling C# Class**: `{def.DefType}` in `{string.Join(", ", typePaths.Select(Path.GetFileName))}`");

            sb.AppendLine($"- **Source File**: `{def.FilePath}`");

            var resolvedXmlStr = await XmlInheritanceHelper.ResolveDefXmlAsync(name, _defIndexer);
            sb.AppendLine("\n## Resolved XML (with Inheritance)");
            sb.AppendLine("> This shows the final merged values after inheritance resolution.");
            
            var xmlLines = resolvedXmlStr.Split('\n');
            if (xmlLines.Length > 300)
            {
                sb.AppendLine("```xml");
                for (int i = 0; i < 200; i++) sb.AppendLine(xmlLines[i]);
                sb.AppendLine($"\n<!-- ... [TRUNCATED {xmlLines.Length - 250} LINES FOR CONTEXT SAFETY] ... -->\n");
                for (int i = xmlLines.Length - 50; i < xmlLines.Length; i++) sb.AppendLine(xmlLines[i]);
                sb.AppendLine("```");
                sb.AppendLine($"> *Note: The XML is very long ({xmlLines.Length} lines). Only the head and tail are shown. Use 'read_code' on the Source File listed above if you need the full content.*");
            }
            else
            {
                sb.AppendLine("```xml");
                sb.AppendLine(resolvedXmlStr);
                sb.AppendLine("```");
            }

            // Semantic Bridge (Comps, Workers, and Attributes -> C# Classes)
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
                    "roomRoleWorker", "statWorker", "moteClass", "thinkTree"
                };

                var foundTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var el in xdoc.Descendants())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    // Specific tags known to hold class names
                    if (classTags.Contains(el.Name.LocalName) || 
                        el.Name.LocalName.EndsWith("Class", StringComparison.OrdinalIgnoreCase) ||
                        el.Name.LocalName.EndsWith("Worker", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = el.Value.Trim();
                        if (!string.IsNullOrEmpty(val)) foundTypes.Add(val);
                    }

                    // 'Class' attributes used for polymorphism (Comps, HediffComps, etc.)
                    var classAttr = el.Attribute("Class");
                    if (classAttr != null)
                    {
                        var val = classAttr.Value.Trim();
                        if (!string.IsNullOrEmpty(val)) foundTypes.Add(val);
                    }
                }

                if (foundTypes.Count > 0)
                {
                    sb.AppendLine("\n## Linked C# Types (Logic & Comps)");
                    foreach (var cls in foundTypes)
                    {
                        var paths = _sourceIndexer.GetPathsByType(cls);
                        if (paths.Count > 0)
                            sb.AppendLine($"- **{cls}**: `{string.Join(", ", paths.Select(Path.GetFileName))}` - Use 'read_code' to see implementation.");
                        else
                            sb.AppendLine($"- **{cls}** (Type not found in current source indexing)");
                    }
                }
            }
            catch
            {
                // XML parsing failed or no classes found, silent skip
            }

            return new ToolResult(sb.ToString());
        }

        //  Try to resolve as a C# Type.
        var csharpPaths = _sourceIndexer.GetPathsByType(name);
        if (csharpPaths.Count > 0)
        {
            sb.AppendLine($"# C# Type Analysis: {name}");

            var chain = _sourceIndexer.GetInheritanceChain(name);
            if (chain.Count > 0)
            {
                sb.AppendLine("\n## Inheritance Graph");
                sb.AppendLine("```mermaid\ngraph TD");
                foreach (var (child, parent) in chain) sb.AppendLine($"    {child} --> {parent}");
                sb.AppendLine("```\n");
            }

            foreach (var path in csharpPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.AppendLine($"## Outline (File: `{path}`)");
                sb.AppendLine(await RoslynHelper.GetClassOutlineAsync(path, name));
                sb.AppendLine("---");
            }

            return new ToolResult(sb.ToString());
        }

        return new ToolResult(
            $"Resource '{name}' not found. Tips: 1. If you aren't sure of the exact name, use 'locate' tool first. 2. Remember that class names are case-sensitive.",
            true);
    }
}
