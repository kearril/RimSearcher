using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace RimSearcher.Core;

public record DefLocation(string FilePath, string DefType, string DefName, string? ParentName, string? Label, bool IsAbstract = false);

public class DefIndexer
{
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<string, DefLocation> _defNameIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DefLocation> _parentNameIndex = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, ConcurrentBag<DefLocation>> _labelIndex =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, ConcurrentBag<(DefLocation Location, string FieldPath)>> _fieldContentIndex =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, byte> _processedFiles = new(StringComparer.OrdinalIgnoreCase);
    private static readonly XmlReaderSettings SafeSettings = new() { DtdProcessing = DtdProcessing.Prohibit };

    public DefIndexer(ILogger? logger = null)
    {
        _logger = logger;
    }

    public void Scan(string rootPath)
    {
        if (!Directory.Exists(rootPath)) return;
        var blacklistedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "bin", "obj", ".git", ".vs", ".idea", ".build", "temp" };

        var allFiles = new List<string>();
        var stack = new Stack<string>();
        stack.Push(rootPath);

        while (stack.Count > 0)
        {
            var currentPath = stack.Pop();
            try
            {
                foreach (var file in Directory.GetFiles(currentPath, "*.xml")) allFiles.Add(file);
                foreach (var dir in Directory.GetDirectories(currentPath))
                {
                    if (!blacklistedDirs.Contains(Path.GetFileName(dir))) stack.Push(dir);
                }
            }
            catch { }
        }

        var newFiles = allFiles.Where(f => _processedFiles.TryAdd(Path.GetFullPath(f), 0)).ToList();
        int totalParsed = 0;

        Parallel.ForEach(newFiles, file =>
        {
            var internedFile = string.Intern(file);
            
            try
            {
                using var stream = new FileStream(internedFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = XmlReader.Create(stream, SafeSettings);

                if (!reader.ReadToFollowing("Defs")) return;

                int nodeIdx = 0;
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.Depth == 1)
                    {
                        nodeIdx++;
                        string defType = reader.LocalName;
                        string? nameAttr = reader.GetAttribute("Name");
                        string? parentNameAttr = reader.GetAttribute("ParentName");
                        string? abstractAttr = reader.GetAttribute("Abstract");
                        bool isAbstract = string.Equals(abstractAttr, "true", StringComparison.OrdinalIgnoreCase);

                        string? defName = null;
                        string? label = null;

                        if (!reader.IsEmptyElement)
                        {
                            int defDepth = reader.Depth;
                            while (reader.Read() && reader.Depth > defDepth)
                            {
                                if (reader.NodeType == XmlNodeType.Element)
                                {
                                    if (reader.LocalName == "defName")
                                        defName = reader.ReadElementContentAsString();
                                    else if (reader.LocalName == "label")
                                        label = reader.ReadElementContentAsString();
                                }
                            }
                        }

                        string identifier = defName ?? nameAttr ?? $"[Unnamed_{defType}_{nodeIdx}]";
                        var loc = new DefLocation(internedFile, defType, identifier, parentNameAttr, label, isAbstract);

                        if (!string.IsNullOrEmpty(defName)) _defNameIndex[defName] = loc;
                        if (!string.IsNullOrEmpty(nameAttr)) _parentNameIndex[nameAttr] = loc;
                        if (!string.IsNullOrEmpty(label))
                        {
                            var bag = _labelIndex.GetOrAdd(label, _ => new ConcurrentBag<DefLocation>());
                            bag.Add(loc);
                        }

                        Interlocked.Increment(ref totalParsed);
                    }
                }

                IndexFieldContents(internedFile);
            }
            catch { }
        });

        if (_logger != null && totalParsed > 0)
        {
            _logger.LogInformation("DefIndexer: Parsed {Count} defs", totalParsed);
        }
    }

    public XDocument GetOrLoadDocument(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = XmlReader.Create(stream, SafeSettings);
        return XDocument.Load(reader);
    }

    private void IndexFieldContents(string filePath)
    {
        try
        {
            var doc = GetOrLoadDocument(filePath);
            if (doc.Root == null) return;

            foreach (var defElement in doc.Root.Elements())
            {
                var defName = defElement.Element("defName")?.Value ?? defElement.Attribute("Name")?.Value;
                if (string.IsNullOrEmpty(defName)) continue;

                if (!_defNameIndex.TryGetValue(defName, out var location))
                {
                    if (!_parentNameIndex.TryGetValue(defName, out location))
                        continue;
                }

                IndexElementRecursive(defElement, location, "", 0);
            }
        }
        catch { }
    }

    private void IndexElementRecursive(XElement element, DefLocation location, string pathPrefix, int depth = 0)
    {
        if (depth >= 3) return;
        
        var currentPath = string.IsNullOrEmpty(pathPrefix)
            ? element.Name.LocalName
            : $"{pathPrefix}.{element.Name.LocalName}";

        var elementName = element.Name.LocalName;
        if (elementName.Length >= 3)
        {
            _fieldContentIndex.GetOrAdd(elementName.ToLowerInvariant(), _ => new ConcurrentBag<(DefLocation, string)>())
                .Add((location, currentPath));
        }

        if (!element.HasElements && !string.IsNullOrWhiteSpace(element.Value))
        {
            var value = element.Value.Trim();
            var words = Regex.Split(value, @"\W+")
                .Where(w => w.Length >= 3)
                .Select(w => w.ToLowerInvariant())
                .Distinct();

            foreach (var word in words)
            {
                _fieldContentIndex.GetOrAdd(word, _ => new ConcurrentBag<(DefLocation, string)>())
                    .Add((location, currentPath));
            }
        }

        foreach (var child in element.Elements())
        {
            IndexElementRecursive(child, location, currentPath, depth + 1);
        }
    }

    public List<(DefLocation Location, double Score)> FuzzySearch(string query)
    {
        var scoredResults = _defNameIndex
            .Select(kv => new
            {
                Loc = kv.Value,
                Score = FuzzyMatcher.CalculateFuzzyScore(kv.Key, query) * 1.2 * (kv.Value.IsAbstract ? 0.5 : 1.0)
            })
            .Concat(_parentNameIndex.Select(kv => new
            {
                Loc = kv.Value,
                Score = FuzzyMatcher.CalculateFuzzyScore(kv.Key, query) * 1.0 * (kv.Value.IsAbstract ? 0.5 : 1.0)
            }))
            .Concat(_labelIndex.SelectMany(kv =>
                kv.Value.Select(loc => new
                {
                    Loc = loc,
                    Score = FuzzyMatcher.CalculateFuzzyScore(kv.Key, query) * 0.8 * (loc.IsAbstract ? 0.5 : 1.0)
                })))
            .Where(x => x.Score > 0)
            .GroupBy(x => $"{x.Loc.DefType}/{x.Loc.DefName}")
            .Select(g => g.OrderByDescending(x => x.Score).First())
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Loc.DefName.Length)
            .Take(50)
            .Select(x => (x.Loc, x.Score))
            .ToList();

        return scoredResults;
    }

    public List<(DefLocation Location, List<string> MatchedFields)> SearchByContent(string[] keywords)
    {
        if (keywords == null || keywords.Length == 0)
            return new List<(DefLocation, List<string>)>();

        var matchedDefs = new Dictionary<string, (DefLocation Location, HashSet<string> FieldPaths, int MatchCount)>();

        foreach (var keyword in keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword) || keyword.Length < 3)
                continue;

            var keyLower = keyword.ToLowerInvariant();

            if (_fieldContentIndex.TryGetValue(keyLower, out var matches))
            {
                foreach (var (location, fieldPath) in matches)
                {
                    var defKey = $"{location.DefType}/{location.DefName}";

                    if (!matchedDefs.TryGetValue(defKey, out var existing))
                    {
                        existing = (location, new HashSet<string>(), 0);
                        matchedDefs[defKey] = existing;
                    }

                    existing.FieldPaths.Add(fieldPath);
                    existing.MatchCount++;
                    matchedDefs[defKey] = existing;
                }
            }
        }

        return matchedDefs.Values
            .OrderByDescending(x => x.MatchCount)
            .ThenBy(x => x.Location.DefName.Length)
            .Take(30)
            .Select(x => (x.Location, x.FieldPaths.ToList()))
            .ToList();
    }

    public DefLocation? GetDef(string name) => _defNameIndex.TryGetValue(name, out var loc)
        ? loc
        : (_parentNameIndex.TryGetValue(name, out var locP) ? locP : null);

    public DefLocation? GetParent(string parentName) =>
        _parentNameIndex.TryGetValue(parentName, out var loc) ? loc : null;
}