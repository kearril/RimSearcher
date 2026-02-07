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
    public string Description => "Deeply analyzes RimWorld resources. Core features: 1. Resolves XML inheritance (ParentName) to show final merged values; 2. Extracts associated C# classes (thingClass, workerClass, etc.) from Defs; 3. Displays full inheritance chains and class outlines for C# types. PREFER this tool when analyzing the structure of a specific Def or Class.";

    public object JsonSchema => new {
        type = "object",
        properties = new {
            name = new { 
                type = "string", 
                description = "The exact DefName or Class name to inspect. Example: 'Apparel_ShieldBelt' or 'RimWorld.Pawn'." 
            }
        },
        required = new[] { "name" }
    };

    public async Task<string> ExecuteAsync(JsonElement args)
    {
        var name = args.GetProperty("name").GetString();
        if (string.IsNullOrEmpty(name)) return "Name cannot be empty.";

        var sb = new StringBuilder();

        // Try to resolve as a Def first to get its merged XML content.
        var def = _defIndexer.GetDef(name);
        if (def != null)
        {
            sb.AppendLine($"# Def Analysis: {name}");
            sb.AppendLine($"- **Type**: {def.DefType}");
            sb.AppendLine($"- **File**: `{def.FilePath}`");
            
            var resolvedXmlStr = await XmlInheritanceHelper.ResolveDefXmlAsync(name, _defIndexer);
            sb.AppendLine("\n## Resolved XML (with Inheritance)");
            sb.AppendLine("> This shows the final values after merging ParentName templates.");
            sb.AppendLine("```xml");
            sb.AppendLine(resolvedXmlStr);
            sb.AppendLine("```");

            // Parse XML to extract associated C# types (e.g., thingClass, workerClass, etc.).
            try
            {
                var xdoc = XDocument.Parse(resolvedXmlStr);
                
                // Define tags commonly used for class associations in RimWorld.
                var classTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
                { 
                    "thingClass", "workerClass", "jobClass", "hediffClass", "thoughtClass", 
                    "compClass", "incidentClass", "interactionWorkerClass", "mentalStateHandlerClass",
                    "ritualBehaviorClass", "skillGiverClass", "worldObjectClass", "lifeStageWorkerClass",
                    "terrainDef", "traitWorkerClass", "selectionWorkerClass", "modExtension", "giverClass",
                    "pathFilters", "soundClass", "damageWorkerClass", "linkDrawerClass"
                };
                
                var foundTypes = new HashSet<string>();
                foreach (var el in xdoc.Descendants())
                {
                    // 1. Match known tags.
                    if (classTags.Contains(el.Name.LocalName))
                    {
                        var val = el.Value.Trim();
                        if (!string.IsNullOrEmpty(val)) foundTypes.Add(val);
                    }
                    // 2. Heuristic match: any tag ending in Class or Worker.
                    else if (el.Name.LocalName.EndsWith("Class", StringComparison.OrdinalIgnoreCase) || 
                             el.Name.LocalName.EndsWith("Worker", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = el.Value.Trim();
                        if (!string.IsNullOrEmpty(val)) foundTypes.Add(val);
                    }
                    // 3. Handle XML attribute form.
                    var classAttr = el.Attribute("Class");
                    if (classAttr != null)
                    {
                        foundTypes.Add(classAttr.Value.Trim());
                    }
                }

                if (foundTypes.Count > 0)
                {
                    sb.AppendLine("\n## Associated C# Types");
                    sb.AppendLine("> Use 'read_code' or 'trace' on these types to see their logic.");
                    foreach (var cls in foundTypes)
                    {
                        var paths = _sourceIndexer.GetPathsByType(cls);
                        if (paths.Count > 0)
                        {
                            sb.AppendLine($"- **{cls}**: `{string.Join(", ", paths)}` - Hint: Use 'read_code' here.");
                        }
                        else
                        {
                            sb.AppendLine($"- **{cls}** (Source not found in current project)");
                        }
                    }
                }
            }
            catch { }

            return sb.ToString();
        }

        // 2. Try to resolve as a C# Type.
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
                sb.AppendLine($"## Outline (File: `{path}`)");
                sb.AppendLine("> Use 'read_code' with 'methodName' to see the implementation of any method below.");
                sb.AppendLine(await RoslynHelper.GetClassOutlineAsync(path, name));
                sb.AppendLine("---");
            }
            return sb.ToString();
        }

        return $"Resource '{name}' not found. Tips: 1. If you aren't sure of the exact name, use 'locate' tool first. 2. Remember that class names are case-sensitive and might require namespaces.";
    }
}