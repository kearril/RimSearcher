using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace RimSearcher.Core;

public record DefLocation(string FilePath, string DefType, string DefName, string? ParentName, string? Label);

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
                        var loc = new DefLocation(file, defType, identifier, parentNameAttr, label);

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
        var results = new List<DefLocation>();
        if (_defNameIndex.TryGetValue(query, out var loc1)) results.Add(loc1);
        if (_parentNameIndex.TryGetValue(query, out var loc2)) results.Add(loc2);
        var partialMatches = _defNameIndex.Where(kv => kv.Key.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Concat(_parentNameIndex.Where(kv => kv.Key.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .Take(30).Select(kv => kv.Value);
        results.AddRange(partialMatches);
        var labelMatches = _labelIndex.Where(kv => kv.Key.Contains(query, StringComparison.OrdinalIgnoreCase))
            .SelectMany(kv => kv.Value).Take(20);
        results.AddRange(labelMatches);
        return results.GroupBy(x => $"{x.DefType}/{x.DefName}").Select(g => g.First()).Take(50).ToList();
    }

    public DefLocation? GetDef(string name) => _defNameIndex.TryGetValue(name, out var loc)
        ? loc
        : (_parentNameIndex.TryGetValue(name, out var locP) ? locP : null);

    public DefLocation? GetParent(string parentName) =>
        _parentNameIndex.TryGetValue(parentName, out var loc) ? loc : null;
}