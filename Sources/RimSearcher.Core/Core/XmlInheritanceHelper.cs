using System.Xml;
using System.Xml.Linq;

namespace RimSearcher.Core;

public static class XmlInheritanceHelper
{
    // Configure XML reader settings to prohibit DTD processing, defending against potential XXE attacks.
    private static readonly XmlReaderSettings SafeSettings = new() { DtdProcessing = DtdProcessing.Prohibit };

    // Common RimWorld XML tags that act as list containers. 
    // Children of these tags should be appended rather than merged.
    private static readonly HashSet<string> ListContainerNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "comps", "stages", "modExtensions", "lifeStages", "hediffGivers", 
        "parts", "verbs", "tools", "abilities", "hediffFilters", "disallowedTraits",
        "tags", "weaponTags", "apparelTags", "tradeTags", "thoughtContexts"
    };

    public static async Task<string> ResolveDefXmlAsync(string defName, DefIndexer indexer)
    {
        var targetLoc = indexer.GetDef(defName);
        if (targetLoc == null) return "Def not found";

        var hierarchy = new Stack<XElement>();
        var currentLoc = targetLoc;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (currentLoc != null)
        {
            if (!visited.Add(currentLoc.DefName + currentLoc.FilePath)) break;
            if (visited.Count > 15) break;
            if (!PathSecurity.IsPathSafe(currentLoc.FilePath)) break;

            try
            {
                var doc = indexer.GetOrLoadDocument(currentLoc.FilePath);
                XElement? node = null;
                var nodes = doc.Root?.Elements() ?? Enumerable.Empty<XElement>();
                foreach (var n in nodes)
                {
                    if (n.Element("defName")?.Value == currentLoc.DefName ||
                        n.Attribute("Name")?.Value == currentLoc.DefName)
                    {
                        node = n;
                        break;
                    }
                }

                if (node != null)
                {
                    hierarchy.Push(new XElement(node));
                    var parentName = currentLoc.ParentName;
                    currentLoc = !string.IsNullOrEmpty(parentName)
                        ? (indexer.GetParent(parentName) ?? indexer.GetDef(parentName))
                        : null;
                }
                else break;
            }
            catch
            {
                break;
            }
        }

        if (hierarchy.Count == 0) return "Failed to load Def XML";

        XElement result = new XElement(hierarchy.Peek().Name);
        while (hierarchy.Count > 0) MergeXml(result, hierarchy.Pop());

        // Ensure defName, label and description are at the top for better readability
        var defNameEl = result.Element("defName");
        var labelEl = result.Element("label");
        var descEl = result.Element("description");
        
        // Remove them first
        defNameEl?.Remove();
        labelEl?.Remove();
        descEl?.Remove();

        // Add them back in reverse order of priority to ensure: defName, then label, then description
        if (descEl != null) result.AddFirst(descEl);
        if (labelEl != null) result.AddFirst(labelEl);
        if (defNameEl != null) result.AddFirst(defNameEl);

        CleanupMetadata(result);
        return result.ToString();
    }

    private static void CleanupMetadata(XElement element)
    {
        // Only remove 'Name' if 'defName' element exists, to keep identity for abstract defs.
        if (element.Element("defName") != null)
        {
            element.Attribute("Name")?.Remove();
        }
        
        element.Attribute("ParentName")?.Remove();
        element.Attribute("Abstract")?.Remove();
        element.Attribute("Inherit")?.Remove();

        foreach (var sub in element.Elements())
        {
            CleanupMetadata(sub);
        }
    }

    private static void MergeXml(XElement parent, XElement child)
    {
        // Handle Inherit="false": completely detach from parent.
        bool inherit = child.Attribute("Inherit")?.Value.ToLower() != "false";
        if (!inherit)
        {
            parent.RemoveAttributes();
            parent.RemoveNodes();
            foreach (var attr in child.Attributes().Where(a => a.Name.LocalName != "Inherit"))
                parent.SetAttributeValue(attr.Name, attr.Value);
            foreach (var node in child.Nodes())
                parent.Add(XNode.ReadFrom(node.CreateReader()));
            return;
        }

        // Attribute handling: child attributes replace parent attributes (standard RimWorld behavior).
        parent.RemoveAttributes();
        foreach (var attr in child.Attributes().Where(a => a.Name.LocalName != "Inherit"))
            parent.SetAttributeValue(attr.Name, attr.Value);

        // Simple value override: if child has text but no elements, it wipes parent elements.
        if (!child.Elements().Any() && !string.IsNullOrEmpty(child.Value))
        {
            parent.RemoveNodes();
            parent.Value = child.Value;
            return;
        }

        bool isListContainer = ListContainerNames.Contains(parent.Name.LocalName);

        foreach (var childNode in child.Elements())
        {
            
            if (childNode.Name.LocalName == "li" || isListContainer)
            {
                parent.Add(new XElement(childNode));
                continue;
            }

            var existingNode = parent.Element(childNode.Name);
            if (existingNode != null) MergeXml(existingNode, childNode);
            else parent.Add(new XElement(childNode));
        }
    }
}