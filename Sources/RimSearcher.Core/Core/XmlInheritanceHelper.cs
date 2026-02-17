using System.Xml;
using System.Xml.Linq;

namespace RimSearcher.Core;

public static class XmlInheritanceHelper
{
    private static readonly XmlReaderSettings SafeSettings = new() { DtdProcessing = DtdProcessing.Prohibit };

    private static readonly HashSet<string> ListContainerNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "comps", "stages", "modExtensions", "lifeStages", "hediffGivers",
        "parts", "verbs", "tools", "abilities", "hediffFilters", "disallowedTraits",
        "tags", "weaponTags", "apparelTags", "tradeTags", "thoughtContexts",
        "recipeUsers", "thingCategories", "researchPrerequisites", "skillRequirements",
        "descriptionHyperlinks", "forcedTraits", "disallowedTraitsWithDegree",
        "nullifyingTraitDegrees", "agreeableTraits", "disagreeableTraits",
        "disallowedThingDefs", "apparelRequired", "techHediffsRequired", "fixedInventory",
        "requirementSet", "fixedIngredientFilter", "defaultIngredientFilter",
        "requirementTags", "exclusionTags", "blacklistedGenders", "whiteListedGenders",
        "hediffClassList", "requiredHediffs", "requiredGeneDefs", "disallowedGenes",
        "startingResearchProjects", "addDesignators", "addDesignatorGroups"
    };

    /// <summary>
    /// Resolves XML inheritance and returns the merged XElement directly.
    /// Returns null if the def is not found or loading fails.
    /// </summary>
    public static async Task<XElement?> ResolveDefXmlElementAsync(string defName, DefIndexer indexer)
    {
        var targetLoc = indexer.GetDef(defName);
        if (targetLoc == null) return null;

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

        if (hierarchy.Count == 0) return null;

        XElement result = new XElement(hierarchy.Peek().Name);
        while (hierarchy.Count > 0) MergeXml(result, hierarchy.Pop());

        var defNameEl = result.Element("defName");
        var labelEl = result.Element("label");
        var descEl = result.Element("description");

        defNameEl?.Remove();
        labelEl?.Remove();
        descEl?.Remove();

        if (descEl != null) result.AddFirst(descEl);
        if (labelEl != null) result.AddFirst(labelEl);
        if (defNameEl != null) result.AddFirst(defNameEl);

        CleanupMetadata(result);
        return result;
    }

    /// <summary>
    /// Legacy convenience method that returns the resolved XML as a string.
    /// </summary>
    public static async Task<string> ResolveDefXmlAsync(string defName, DefIndexer indexer)
    {
        var element = await ResolveDefXmlElementAsync(defName, indexer);
        return element?.ToString() ?? "Def not found";
    }

    private static void CleanupMetadata(XElement element)
    {
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
        bool inherit = child.Attribute("Inherit")?.Value.ToLower() != "false";
        if (!inherit)
        {
            parent.RemoveAttributes();
            parent.RemoveNodes();
            foreach (var attr in child.Attributes().Where(a => a.Name.LocalName != "Inherit"))
                parent.SetAttributeValue(attr.Name, attr.Value);
            foreach (var node in child.Nodes())
                parent.Add(node);
            return;
        }

        parent.RemoveAttributes();
        foreach (var attr in child.Attributes().Where(a => a.Name.LocalName != "Inherit"))
            parent.SetAttributeValue(attr.Name, attr.Value);

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