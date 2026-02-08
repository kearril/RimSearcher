using System.Xml;
using System.Xml.Linq;

namespace RimSearcher.Core;

public static class XmlInheritanceHelper
{
    // Configure XML reader settings to prohibit DTD processing, defending against potential XXE attacks.
    private static readonly XmlReaderSettings SafeSettings = new() { DtdProcessing = DtdProcessing.Prohibit };

    public static async Task<string> ResolveDefXmlAsync(string defName, DefIndexer indexer)
    {
        var targetLoc = indexer.GetDef(defName);
        if (targetLoc == null) return "Def not found";

        var hierarchy = new Stack<XElement>();
        var currentLoc = targetLoc;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileCache = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);

        while (currentLoc != null)
        {
            if (!visited.Add(currentLoc.DefName + currentLoc.FilePath)) break;
            if (visited.Count > 15) break;
            if (!PathSecurity.IsPathSafe(currentLoc.FilePath)) break;

            try
            {
                if (!fileCache.TryGetValue(currentLoc.FilePath, out var doc))
                {
                    using var stream = new FileStream(currentLoc.FilePath, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite);
                    using var reader = XmlReader.Create(stream, SafeSettings);
                    doc = XDocument.Load(reader);
                    fileCache[currentLoc.FilePath] = doc;
                }

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
        return result.ToString();
    }

    private static void MergeXml(XElement parent, XElement child)
    {
        foreach (var attr in child.Attributes()) parent.SetAttributeValue(attr.Name, attr.Value);
        bool inherit = child.Attribute("Inherit")?.Value.ToLower() != "false";
        var childElements = child.Elements().ToList();
        if (!inherit)
        {
            var elementNames = childElements.Select(e => e.Name).Distinct();
            foreach (var name in elementNames) parent.Elements(name).Remove();
        }

        foreach (var childNode in childElements)
        {
            if (childNode.Name.LocalName == "li")
            {
                parent.Add(new XElement(childNode));
                continue;
            }

            var existingNode = parent.Element(childNode.Name);
            if (existingNode != null && childNode.HasElements) MergeXml(existingNode, childNode);
            else if (existingNode != null) existingNode.Value = childNode.Value;
            else parent.Add(new XElement(childNode));
        }
    }
}