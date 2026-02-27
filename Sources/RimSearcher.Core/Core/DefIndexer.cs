using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace RimSearcher.Core;

public record DefLocation(string FilePath, string DefType, string DefName, string? ParentName, string? Label, bool IsAbstract = false);

public partial class DefIndexer
{
    [GeneratedRegex(@"\W+")]
    private static partial Regex WordSplitRegex();
    
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<string, DefLocation> _defNameIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DefLocation> _parentNameIndex = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, ConcurrentBag<DefLocation>> _labelIndex =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, ConcurrentBag<(DefLocation Location, string FieldPath)>> _fieldContentIndex =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, byte> _processedFiles = new(StringComparer.OrdinalIgnoreCase);
    private static readonly XmlReaderSettings SafeSettings = new() { DtdProcessing = DtdProcessing.Prohibit };
    
    private FrozenDictionary<string, DefLocation>? _frozenDefNameIndex;
    private FrozenDictionary<string, DefLocation>? _frozenParentNameIndex;
    private FrozenDictionary<string, DefLocation[]>? _frozenLabelIndex;
    private FrozenDictionary<string, (DefLocation Location, string FieldPath)[]>? _frozenFieldContentIndex;

    public DefIndexer(ILogger? logger = null)
    {
        _logger = logger;
    }
    
    public void FreezeIndex()
    {
        _frozenDefNameIndex = _defNameIndex.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _frozenParentNameIndex = _parentNameIndex.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _frozenLabelIndex = _labelIndex.ToFrozenDictionary(
            kv => kv.Key, kv => kv.Value.Distinct().ToArray(), StringComparer.OrdinalIgnoreCase);
        _frozenFieldContentIndex = _fieldContentIndex.ToFrozenDictionary(
            kv => kv.Key, kv => kv.Value.Distinct().ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    public DefIndexerSnapshot ExportSnapshot()
    {
        var labelIndex = _frozenLabelIndex != null
            ? _frozenLabelIndex.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            : _labelIndex.ToDictionary(kv => kv.Key, kv => kv.Value.Distinct().ToArray(), StringComparer.OrdinalIgnoreCase);

        Dictionary<string, DefFieldContentSnapshot[]> fieldContentIndex;
        if (_frozenFieldContentIndex != null)
        {
            fieldContentIndex = _frozenFieldContentIndex.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Select(entry => new DefFieldContentSnapshot
                {
                    Location = entry.Location,
                    FieldPath = entry.FieldPath
                }).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            fieldContentIndex = _fieldContentIndex.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Distinct().Select(entry => new DefFieldContentSnapshot
                {
                    Location = entry.Location,
                    FieldPath = entry.FieldPath
                }).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        }

        var defNameIndex = _frozenDefNameIndex != null
            ? _frozenDefNameIndex.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, DefLocation>(_defNameIndex, StringComparer.OrdinalIgnoreCase);

        var parentNameIndex = _frozenParentNameIndex != null
            ? _frozenParentNameIndex.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, DefLocation>(_parentNameIndex, StringComparer.OrdinalIgnoreCase);

        return new DefIndexerSnapshot
        {
            DefNameIndex = defNameIndex,
            ParentNameIndex = parentNameIndex,
            LabelIndex = labelIndex,
            FieldContentIndex = fieldContentIndex,
            ProcessedFiles = _processedFiles.Keys.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    public void ImportSnapshot(DefIndexerSnapshot snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

        _defNameIndex.Clear();
        _parentNameIndex.Clear();
        _labelIndex.Clear();
        _fieldContentIndex.Clear();
        _processedFiles.Clear();
        ResetFrozenState();

        foreach (var (key, value) in snapshot.DefNameIndex)
        {
            _defNameIndex[key] = value;
        }

        foreach (var (key, value) in snapshot.ParentNameIndex)
        {
            _parentNameIndex[key] = value;
        }

        foreach (var (key, values) in snapshot.LabelIndex)
        {
            var deduped = values.Distinct().ToArray();
            _labelIndex[key] = new ConcurrentBag<DefLocation>(deduped);
        }

        foreach (var (key, values) in snapshot.FieldContentIndex)
        {
            var deduped = values
                .Select(entry => (entry.Location, entry.FieldPath))
                .Distinct()
                .ToArray();
            _fieldContentIndex[key] = new ConcurrentBag<(DefLocation Location, string FieldPath)>(deduped);
        }

        foreach (var file in snapshot.ProcessedFiles.Where(file => !string.IsNullOrWhiteSpace(file)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _processedFiles[file] = 0;
        }
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
                var doc = GetOrLoadDocument(internedFile);
                if (doc.Root == null || doc.Root.Name.LocalName != "Defs") return;

                int nodeIdx = 0;
                foreach (var defElement in doc.Root.Elements())
                {
                    nodeIdx++;
                    string defType = defElement.Name.LocalName;
                    string? nameAttr = defElement.Attribute("Name")?.Value;
                    string? parentNameAttr = defElement.Attribute("ParentName")?.Value;
                    string? abstractAttr = defElement.Attribute("Abstract")?.Value;
                    bool isAbstract = string.Equals(abstractAttr, "true", StringComparison.OrdinalIgnoreCase);

                    string? defName = defElement.Element("defName")?.Value;
                    string? label = defElement.Element("label")?.Value;

                    string identifier = defName ?? nameAttr ?? $"[Unnamed_{defType}_{nodeIdx}]";
                    var loc = new DefLocation(internedFile, defType, identifier, parentNameAttr, label, isAbstract);

                    if (!string.IsNullOrEmpty(defName)) _defNameIndex[defName] = loc;
                    if (!string.IsNullOrEmpty(nameAttr)) _parentNameIndex[nameAttr] = loc;
                    if (!string.IsNullOrEmpty(label))
                    {
                        _labelIndex.GetOrAdd(label, _ => new ConcurrentBag<DefLocation>()).Add(loc);
                    }

                    IndexElementRecursive(defElement, loc, "", 0);

                    Interlocked.Increment(ref totalParsed);
                }
            }
            catch { }
        });

        if (_logger != null && totalParsed > 0)
        {
            _logger.LogInformation("DefIndexer: XML scan parsed {Count} defs", totalParsed);
        }
    }

    public XDocument GetOrLoadDocument(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = XmlReader.Create(stream, SafeSettings);
        return XDocument.Load(reader);
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
            var words = WordSplitRegex().Split(value)
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
        var defNameSource = (IEnumerable<KeyValuePair<string, DefLocation>>?)_frozenDefNameIndex ?? _defNameIndex;
        var parentNameSource = (IEnumerable<KeyValuePair<string, DefLocation>>?)_frozenParentNameIndex ?? _parentNameIndex;
        
        var scoredResults = defNameSource
            .Select(kv => new
            {
                Loc = kv.Value,
                Score = FuzzyMatcher.CalculateFuzzyScore(kv.Key, query) * 1.2 * (kv.Value.IsAbstract ? 0.5 : 1.0)
            })
            .Concat(parentNameSource.Select(kv => new
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

            IEnumerable<(DefLocation Location, string FieldPath)>? matches = null;
            if (_frozenFieldContentIndex != null && _frozenFieldContentIndex.TryGetValue(keyLower, out var frozenMatches))
                matches = frozenMatches;
            else if (_fieldContentIndex.TryGetValue(keyLower, out var bagMatches))
                matches = bagMatches;

            if (matches != null)
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

    public DefLocation? GetDef(string name)
    {
        if (_frozenDefNameIndex != null)
        {
            if (_frozenDefNameIndex.TryGetValue(name, out var loc)) return loc;
            if (_frozenParentNameIndex != null && _frozenParentNameIndex.TryGetValue(name, out var locP)) return locP;
            return null;
        }
        return _defNameIndex.TryGetValue(name, out var loc2)
            ? loc2
            : (_parentNameIndex.TryGetValue(name, out var locP2) ? locP2 : null);
    }

    public DefLocation? GetParent(string parentName)
    {
        if (_frozenParentNameIndex != null && _frozenParentNameIndex.TryGetValue(parentName, out var loc)) return loc;
        return _parentNameIndex.TryGetValue(parentName, out var loc2) ? loc2 : null;
    }

    private void ResetFrozenState()
    {
        _frozenDefNameIndex = null;
        _frozenParentNameIndex = null;
        _frozenLabelIndex = null;
        _frozenFieldContentIndex = null;
    }
}
