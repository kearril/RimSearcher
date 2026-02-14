using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.Linq;

namespace RimSearcher.Core;

public record DefLocation(string FilePath, string DefType, string DefName, string? ParentName, string? Label, bool IsAbstract = false);

public class DefIndexer
{
    private readonly ConcurrentDictionary<string, DefLocation> _defNameIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DefLocation> _parentNameIndex = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, ConcurrentBag<DefLocation>> _labelIndex =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, byte> _processedFiles = new(StringComparer.OrdinalIgnoreCase);

    // Global cache for parsed XML documents to speed up inheritance resolution.
    private readonly ConcurrentDictionary<string, XDocument> _documentCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly XmlReaderSettings SafeSettings = new() { DtdProcessing = DtdProcessing.Prohibit };

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
            catch
            {
            }
        }

        var newFiles = allFiles.Where(f => _processedFiles.TryAdd(Path.GetFullPath(f), 0)).ToList();
        int totalParsed = 0;

        Parallel.ForEach(newFiles, file =>
        {
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = XmlReader.Create(stream, SafeSettings);
                
                // Fast forward to the root <Defs> element
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
                        var loc = new DefLocation(file, defType, identifier, parentNameAttr, label, isAbstract);

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
            }
            catch
            {
                // Skip malformed XML files
            }
        });

        Console.Error.WriteLine($"[DefIndexer] Scanning complete. Successfully parsed: {totalParsed} Defs");
    }

    public XDocument GetOrLoadDocument(string filePath)
    {
        return _documentCache.GetOrAdd(filePath, path =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = XmlReader.Create(stream, SafeSettings);
            return XDocument.Load(reader);
        });
    }

    public List<DefLocation> Search(string query)
    {
        var scoredResults = _defNameIndex
            .Select(kv => new { Loc = kv.Value, Score = (double)CalculateScore(kv.Key, query) * 1.2 * (kv.Value.IsAbstract ? 0.5 : 1.0) })
            .Concat(_parentNameIndex.Select(kv => new { Loc = kv.Value, Score = (double)CalculateScore(kv.Key, query) * (kv.Value.IsAbstract ? 0.5 : 1.0) }))
            .Concat(_labelIndex.SelectMany(kv =>
                kv.Value.Select(loc => new { Loc = loc, Score = (double)CalculateScore(kv.Key, query) * 0.8 * (loc.IsAbstract ? 0.5 : 1.0) })))
            .Where(x => x.Score > 0)
            .GroupBy(x => $"{x.Loc.DefType}/{x.Loc.DefName}")
            .Select(g => g.OrderByDescending(x => x.Score).First())
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Loc.DefName.Length)
            .Take(50)
            .Select(x => x.Loc)
            .ToList();

        return scoredResults;
    }

    private static int CalculateScore(string text, string query)
    {
        if (string.Equals(text, query, StringComparison.OrdinalIgnoreCase)) return 100;
        if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 70;
        if (text.Contains(query, StringComparison.OrdinalIgnoreCase)) return 40;
        return 0;
    }

    public DefLocation? GetDef(string name) => _defNameIndex.TryGetValue(name, out var loc)
        ? loc
        : (_parentNameIndex.TryGetValue(name, out var locP) ? locP : null);

    public DefLocation? GetParent(string parentName) =>
        _parentNameIndex.TryGetValue(parentName, out var loc) ? loc : null;
}