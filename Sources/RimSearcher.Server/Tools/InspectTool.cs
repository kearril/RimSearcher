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

    public string Name => "inspect";
    public string Description => "全面解析资源。若是 Def 则返回完整数值和对应 C# 类型；若是类型则返回继承图和大纲。";

    public object JsonSchema => new {
        type = "object",
        properties = new {
            name = new { type = "string", description = "类型名或 DefName" }
        },
        required = new[] { "name" }
    };

    public async Task<string> ExecuteAsync(JsonElement args)
    {
        var name = args.GetProperty("name").GetString();
        if (string.IsNullOrEmpty(name)) return "名称不能为空";

        var sb = new StringBuilder();

        // 优先尝试作为 Def 进行解析，获取其合并后的 XML 内容。
        var def = _defIndexer.GetDef(name);
        if (def != null)
        {
            sb.AppendLine($"## Def: {name} ({def.DefType})");
            var resolvedXmlStr = await XmlInheritanceHelper.ResolveDefXmlAsync(name, _defIndexer);
            sb.AppendLine("### Resolved XML Content:");
            sb.AppendLine("```xml");
            sb.AppendLine(resolvedXmlStr);
            sb.AppendLine("```");

            // 解析 XML 以提取关联的 C# 类型（如 thingClass, workerClass 等）。
            try
            {
                var xdoc = XDocument.Parse(resolvedXmlStr);
                
                // 定义 RimWorld 核心常用的类关联标签。
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
                    // 1. 匹配已知标签。
                    if (classTags.Contains(el.Name.LocalName))
                    {
                        var val = el.Value.Trim();
                        if (!string.IsNullOrEmpty(val)) foundTypes.Add(val);
                    }
                    // 2. 启发式匹配：任何以 Class 或 Worker 结尾的标签。
                    else if (el.Name.LocalName.EndsWith("Class", StringComparison.OrdinalIgnoreCase) || 
                             el.Name.LocalName.EndsWith("Worker", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = el.Value.Trim();
                        if (!string.IsNullOrEmpty(val)) foundTypes.Add(val);
                    }
                    // 3. 处理 XML 属性形式。
                    var classAttr = el.Attribute("Class");
                    if (classAttr != null)
                    {
                        foundTypes.Add(classAttr.Value.Trim());
                    }
                }

                if (foundTypes.Count > 0)
                {
                    sb.AppendLine("\n### Associated C# Types:");
                    foreach (var cls in foundTypes)
                    {
                        var paths = _sourceIndexer.GetPathsByType(cls);
                        if (paths.Count > 0)
                        {
                            sb.AppendLine($"- **{cls}**: {string.Join(", ", paths)}");
                        }
                    }
                }
            }
            catch { }

            return sb.ToString();
        }

        // 2. 尝试作为 C# 类型解析
        var csharpPaths = _sourceIndexer.GetPathsByType(name);
        if (csharpPaths.Count > 0)
        {
            sb.AppendLine($"## C# Type: {name}");
            
            var chain = _sourceIndexer.GetInheritanceChain(name);
            if (chain.Count > 0)
            {
                sb.AppendLine("### Inheritance Graph");
                sb.AppendLine("```mermaid\ngraph TD");
                foreach (var (child, parent) in chain) sb.AppendLine($"    {child} --> {parent}");
                sb.AppendLine("```\n");
            }

            foreach (var path in csharpPaths)
            {
                sb.AppendLine($"### Outline (File: {path})");
                sb.AppendLine(await RoslynHelper.GetClassOutlineAsync(path, name));
                sb.AppendLine("---");
            }
            return sb.ToString();
        }

        return $"未找到名为 {name} 的 Def 或 C# 类型。";
    }
}