using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace RimSearcher.Core;

public class SourceIndexer
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _typeMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _inheritanceMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _inheritorsMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _shortTypeMap = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, ConcurrentBag<(string TypeName, string MemberName, string MemberType, string FilePath)>> _memberIndex =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _ngramIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _processedFiles = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _cachedAllTypeNames = new();
    
    private FrozenDictionary<string, string[]>? _frozenIndex;
    private FrozenDictionary<string, string[]>? _frozenTypeMap;
    private FrozenDictionary<string, string[]>? _frozenInheritorsMap;
    private FrozenDictionary<string, string[]>? _frozenShortTypeMap;
    private FrozenDictionary<string, string[]>? _frozenNgramIndex;
    private FrozenDictionary<string, (string TypeName, string MemberName, string MemberType, string FilePath)[]>? _frozenMemberIndex;
    public bool IsFrozen { get; private set; }
    
    public void FreezeIndex()
    {
        _frozenIndex = _index.ToFrozenDictionary(
            kv => kv.Key, kv => kv.Value.Distinct().ToArray(), StringComparer.OrdinalIgnoreCase);
        _frozenTypeMap = _typeMap.ToFrozenDictionary(
            kv => kv.Key, kv => kv.Value.Distinct().ToArray(), StringComparer.OrdinalIgnoreCase);
        _frozenInheritorsMap = _inheritorsMap.ToFrozenDictionary(
            kv => kv.Key, kv => kv.Value.Distinct().ToArray(), StringComparer.OrdinalIgnoreCase);
        _frozenShortTypeMap = _shortTypeMap.ToFrozenDictionary(
            kv => kv.Key, kv => kv.Value.Distinct().ToArray(), StringComparer.OrdinalIgnoreCase);
        _frozenNgramIndex = _ngramIndex.ToFrozenDictionary(
            kv => kv.Key, kv => kv.Value.Distinct().ToArray(), StringComparer.OrdinalIgnoreCase);
        _frozenMemberIndex = _memberIndex.ToFrozenDictionary(
            kv => kv.Key, 
            kv => kv.Value.Distinct().ToArray(), 
            StringComparer.OrdinalIgnoreCase);
        
        _cachedAllTypeNames = _frozenTypeMap.Keys.Concat(_frozenShortTypeMap.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        
        IsFrozen = true;
    }

    public SourceIndexerSnapshot ExportSnapshot()
    {
        var fileIndex = _frozenIndex != null
            ? _frozenIndex.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            : _index.ToDictionary(kv => kv.Key, kv => kv.Value.Distinct().ToArray(), StringComparer.OrdinalIgnoreCase);

        var typeMap = _frozenTypeMap != null
            ? _frozenTypeMap.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            : _typeMap.ToDictionary(kv => kv.Key, kv => kv.Value.Distinct().ToArray(), StringComparer.OrdinalIgnoreCase);

        var inheritorsMap = _frozenInheritorsMap != null
            ? _frozenInheritorsMap.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            : _inheritorsMap.ToDictionary(kv => kv.Key, kv => kv.Value.Distinct().ToArray(), StringComparer.OrdinalIgnoreCase);

        var shortTypeMap = _frozenShortTypeMap != null
            ? _frozenShortTypeMap.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            : _shortTypeMap.ToDictionary(kv => kv.Key, kv => kv.Value.Distinct().ToArray(), StringComparer.OrdinalIgnoreCase);

        var ngramIndex = _frozenNgramIndex != null
            ? _frozenNgramIndex.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            : _ngramIndex.ToDictionary(kv => kv.Key, kv => kv.Value.Distinct().ToArray(), StringComparer.OrdinalIgnoreCase);

        Dictionary<string, SourceMemberSnapshot[]> memberIndex;
        if (_frozenMemberIndex != null)
        {
            memberIndex = _frozenMemberIndex.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Select(member => new SourceMemberSnapshot
                {
                    TypeName = member.TypeName,
                    MemberName = member.MemberName,
                    MemberType = member.MemberType,
                    FilePath = member.FilePath
                }).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            memberIndex = _memberIndex.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Distinct().Select(member => new SourceMemberSnapshot
                {
                    TypeName = member.TypeName,
                    MemberName = member.MemberName,
                    MemberType = member.MemberType,
                    FilePath = member.FilePath
                }).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        }

        var processedFiles = _processedFiles.Keys.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        return new SourceIndexerSnapshot
        {
            FileIndex = fileIndex,
            TypeMap = typeMap,
            InheritanceMap = new Dictionary<string, string>(_inheritanceMap, StringComparer.OrdinalIgnoreCase),
            InheritorsMap = inheritorsMap,
            ShortTypeMap = shortTypeMap,
            MemberIndex = memberIndex,
            NgramIndex = ngramIndex,
            ProcessedFiles = processedFiles
        };
    }

    public void ImportSnapshot(SourceIndexerSnapshot snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

        _index.Clear();
        _typeMap.Clear();
        _inheritanceMap.Clear();
        _inheritorsMap.Clear();
        _shortTypeMap.Clear();
        _memberIndex.Clear();
        _ngramIndex.Clear();
        _processedFiles.Clear();
        ResetFrozenState();

        foreach (var (key, values) in snapshot.FileIndex)
        {
            _index[key] = ToStringBag(values);
        }

        foreach (var (key, values) in snapshot.TypeMap)
        {
            _typeMap[key] = ToStringBag(values);
        }

        foreach (var (key, value) in snapshot.InheritanceMap)
        {
            _inheritanceMap[key] = value;
        }

        foreach (var (key, values) in snapshot.InheritorsMap)
        {
            _inheritorsMap[key] = ToStringBag(values);
        }

        foreach (var (key, values) in snapshot.ShortTypeMap)
        {
            _shortTypeMap[key] = ToStringBag(values);
        }

        foreach (var (key, values) in snapshot.MemberIndex)
        {
            var entries = values
                .Select(member => (member.TypeName, member.MemberName, member.MemberType, member.FilePath))
                .Distinct()
                .ToArray();
            _memberIndex[key] = new ConcurrentBag<(string TypeName, string MemberName, string MemberType, string FilePath)>(entries);
        }

        foreach (var (key, values) in snapshot.NgramIndex)
        {
            _ngramIndex[key] = ToStringBag(values);
        }

        foreach (var file in snapshot.ProcessedFiles.Where(file => !string.IsNullOrWhiteSpace(file)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _processedFiles[file] = 0;
        }

        _cachedAllTypeNames = _typeMap.Keys.Concat(_shortTypeMap.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Scan(string rootPath)
    {
        if (!Directory.Exists(rootPath)) return;
        var blacklistedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "bin", "obj", ".git", ".vs", ".idea", ".build", "temp" };

        var allFiles = CollectFilesIterative(rootPath, blacklistedDirs);
        var newFiles = allFiles.Where(f => _processedFiles.TryAdd(Path.GetFullPath(f), 0)).ToList();

        Parallel.ForEach(newFiles, file =>
        {
            var internedFile = string.Intern(file);
            var fileName = Path.GetFileNameWithoutExtension(internedFile);
            _index.GetOrAdd(fileName, _ => new ConcurrentBag<string>()).Add(internedFile);

            if (internedFile.EndsWith(".cs"))
            {
                var (inheritance, members) = RoslynHelper.GetClassInfoCombined(internedFile);
                
                foreach (var (fullName, baseType) in inheritance)
                {
                    _typeMap.GetOrAdd(fullName, _ => new ConcurrentBag<string>()).Add(internedFile);
                    var shortName = fullName.Contains('.') ? fullName.Split('.').Last() : fullName;
                    _shortTypeMap.GetOrAdd(shortName, _ => new ConcurrentBag<string>()).Add(fullName);

                    if (!string.IsNullOrEmpty(baseType))
                    {
                        _inheritanceMap[fullName] = baseType;
                        _inheritorsMap.GetOrAdd(baseType, _ => new ConcurrentBag<string>()).Add(fullName);
                    }

                    IndexNgrams(fullName);
                    IndexNgrams(shortName);
                }
                
                IndexMembersFromList(members, internedFile);
            }
        });

        _cachedAllTypeNames = _typeMap.Keys.Concat(_shortTypeMap.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private List<string> CollectFilesIterative(string rootPath, HashSet<string> blacklistedDirs)
    {
        var result = new List<string>();
        var stack = new Stack<string>();
        stack.Push(rootPath);

        while (stack.Count > 0)
        {
            var currentPath = stack.Pop();
            try
            {
                foreach (var file in Directory.GetFiles(currentPath))
                {
                    if (file.EndsWith(".cs") || file.EndsWith(".xml")) result.Add(file);
                }
                foreach (var dir in Directory.GetDirectories(currentPath))
                {
                    if (!blacklistedDirs.Contains(Path.GetFileName(dir))) stack.Push(dir);
                }
            }
            catch { }
        }
        return result;
    }

    private bool TryGetInheritors(string key, out IReadOnlyList<string> values)
    {
        if (_frozenInheritorsMap != null && _frozenInheritorsMap.TryGetValue(key, out var frozen))
        { values = frozen; return true; }
        if (_inheritorsMap.TryGetValue(key, out var bag))
        { values = bag.ToArray(); return true; }
        values = Array.Empty<string>(); return false;
    }
    
    private bool TryGetShortType(string key, out IReadOnlyList<string> values)
    {
        if (_frozenShortTypeMap != null && _frozenShortTypeMap.TryGetValue(key, out var frozen))
        { values = frozen; return true; }
        if (_shortTypeMap.TryGetValue(key, out var bag))
        { values = bag.ToArray(); return true; }
        values = Array.Empty<string>(); return false;
    }
    
    private bool ContainsType(string key) =>
        (_frozenTypeMap?.ContainsKey(key) ?? false) || _typeMap.ContainsKey(key);
    
    private IReadOnlyList<string> GetTypeFiles(string key)
    {
        if (_frozenTypeMap != null && _frozenTypeMap.TryGetValue(key, out var frozen)) return frozen;
        if (_typeMap.TryGetValue(key, out var bag)) return bag.ToArray();
        return Array.Empty<string>();
    }

    public List<string> GetInheritors(string baseTypeName)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (TryGetInheritors(baseTypeName, out var directInheritors))
        {
            foreach (var item in directInheritors) results.Add(item);
        }

        if (TryGetShortType(baseTypeName, out var fullNames))
        {
            foreach (var fullName in fullNames)
            {
                if (TryGetInheritors(fullName, out var inheritors))
                {
                    foreach (var item in inheritors) results.Add(item);
                }
            }
        }

        var shortNameCandidate = baseTypeName.Contains('.') ? baseTypeName.Split('.').Last() : baseTypeName;
        if (shortNameCandidate != baseTypeName)
        {
            if (TryGetInheritors(shortNameCandidate, out var shortInheritors))
            {
                foreach (var item in shortInheritors) results.Add(item);
            }
        }

        return results.ToList();
    }

    public List<(string Child, string Parent)> GetInheritanceChain(string typeName)
    {
        var chain = new List<(string Child, string Parent)>();
        
        string? current = ContainsType(typeName) ? typeName : null;
        if (current == null && TryGetShortType(typeName, out var fullNames))
        {
            current = fullNames.FirstOrDefault();
        }
        
        if (current == null) return chain;

        while (_inheritanceMap.TryGetValue(current, out var parent))
        {
            if (chain.Any(x => x.Child == current)) break;
            chain.Add((current, parent));
            
            current = ContainsType(parent) ? parent : null;
            if (current == null && TryGetShortType(parent, out var parentFullNames))
            {
                current = parentFullNames.FirstOrDefault();
            }
            
            if (current == null || chain.Count > 20) break;
        }
        return chain;
    }

    public List<string> GetPathsByType(string typeName)
    {
        var files = GetTypeFiles(typeName);
        if (files.Count > 0) return files.ToList();
        if (TryGetShortType(typeName, out var fullNames))
        {
            return fullNames.Distinct()
                .SelectMany(fn => GetTypeFiles(fn)).ToList();
        }
        return new List<string>();
    }

    public List<string> Search(string query)
    {
        var source = _frozenIndex ?? (IReadOnlyDictionary<string, string[]>?)null;
        if (source != null)
        {
            return source
                .Select(kv => new { Key = kv.Key, Value = kv.Value, Score = FuzzyMatcher.CalculateFuzzyScore(kv.Key, query) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Key.Length)
                .SelectMany(x => x.Value)
                .Distinct()
                .Take(30)
                .ToList();
        }
        return _index
            .Select(kv => new { Key = kv.Key, Value = kv.Value, Score = FuzzyMatcher.CalculateFuzzyScore(kv.Key, query) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Key.Length)
            .SelectMany(x => x.Value)
            .Distinct()
            .Take(30)
            .ToList();
    }

    private void IndexMembersFromList(List<(string TypeName, string MemberName, string MemberType)> members, string filePath)
    {
        foreach (var (typeName, memberName, memberType) in members)
        {
            var words = FuzzyMatcher.SplitIntoWords(memberName);
            foreach (var word in words)
            {
                if (word.Length >= 2)
                {
                    _memberIndex.GetOrAdd(word.ToLowerInvariant(), _ => new ConcurrentBag<(string, string, string, string)>())
                        .Add((typeName, memberName, memberType, filePath));
                }
            }
            _memberIndex.GetOrAdd(memberName.ToLowerInvariant(), _ => new ConcurrentBag<(string, string, string, string)>())
                .Add((typeName, memberName, memberType, filePath));
        }
    }

    private void IndexNgrams(string name)
    {
        if (string.IsNullOrEmpty(name)) return;
        var ngrams = FuzzyMatcher.GenerateNgrams(name, 2).Distinct();
        foreach (var ngram in ngrams)
        {
            _ngramIndex.GetOrAdd(ngram, _ => new ConcurrentBag<string>()).Add(name);
        }
    }

    public List<(string Name, double Score)> FuzzySearchTypes(string query)
    {
        HashSet<string> searchSet;

        if (query.Length <= 4)
        {
            searchSet = new HashSet<string>(_cachedAllTypeNames, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            var queryNgrams = FuzzyMatcher.GenerateNgrams(query, 2).Distinct().ToList();
            var candidateScores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var ngram in queryNgrams)
            {
                IEnumerable<string>? names = null;
                if (_frozenNgramIndex != null && _frozenNgramIndex.TryGetValue(ngram, out var frozenNames))
                    names = frozenNames;
                else if (_ngramIndex.TryGetValue(ngram, out var namesBag))
                    names = namesBag.Distinct();
                    
                if (names != null)
                {
                    foreach (var name in names)
                        candidateScores[name] = candidateScores.GetValueOrDefault(name) + 1;
                }
            }

            if (candidateScores.Count < 50)
            {
                searchSet = new HashSet<string>(_cachedAllTypeNames, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                searchSet = new HashSet<string>(
                    candidateScores.OrderByDescending(kv => kv.Value).Take(500).Select(kv => kv.Key),
                    StringComparer.OrdinalIgnoreCase
                );
            }
        }

        return searchSet
            .Select(name => new { Name = name, Score = FuzzyMatcher.CalculateFuzzyScore(name, query) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Name.Length)
            .Take(20)
            .Select(x => (x.Name, x.Score))
            .ToList();
    }

    public List<(string TypeName, string MemberName, string MemberType, string FilePath, double Score)> SearchMembersByKeywords(string[] keywords)
    {
        if (keywords == null || keywords.Length == 0) return new List<(string, string, string, string, double)>();
        var matchedMembers = new Dictionary<(string, string, string, string), int>();
        
        var memberKeys = _frozenMemberIndex != null 
            ? (IEnumerable<string>)_frozenMemberIndex.Keys 
            : _memberIndex.Keys;

        foreach (var keyword in keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword) || keyword.Length < 2) continue;
            var keyLower = keyword.ToLowerInvariant();

            IEnumerable<(string TypeName, string MemberName, string MemberType, string FilePath)>? members = null;
            if (_frozenMemberIndex != null && _frozenMemberIndex.TryGetValue(keyLower, out var frozenMembers))
                members = frozenMembers;
            else if (_memberIndex.TryGetValue(keyLower, out var bagMembers))
                members = bagMembers;
                
            if (members != null)
            {
                foreach (var member in members)
                {
                    var key = (member.TypeName, member.MemberName, member.MemberType, member.FilePath);
                    matchedMembers[key] = matchedMembers.GetValueOrDefault(key) + 1;
                }
            }

            IEnumerable<string> fuzzyCandidates;
            if (keyLower.Length >= 3)
            {
                var ngrams = FuzzyMatcher.GenerateNgrams(keyLower, 2).Distinct().ToList();
                var candidateSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var ngram in ngrams)
                {
                    foreach (var mk in memberKeys)
                    {
                        if (mk.Contains(ngram, StringComparison.OrdinalIgnoreCase))
                            candidateSet.Add(mk);
                        if (candidateSet.Count >= 200) break;
                    }
                    if (candidateSet.Count >= 200) break;
                }
                fuzzyCandidates = candidateSet;
            }
            else
            {
                fuzzyCandidates = memberKeys.Where(k => k.StartsWith(keyLower, StringComparison.OrdinalIgnoreCase)).Take(50);
            }
            
            var fuzzyMatches = fuzzyCandidates
                .Select(k => (Key: k, Score: FuzzyMatcher.CalculateFuzzyScore(k, keyLower)))
                .Where(x => x.Score >= 60.0)
                .OrderByDescending(x => x.Score)
                .Take(10)
                .Select(x => x.Key);
                
            foreach (var fuzzyKey in fuzzyMatches)
            {
                IEnumerable<(string TypeName, string MemberName, string MemberType, string FilePath)>? fuzzyMemberList = null;
                if (_frozenMemberIndex != null && _frozenMemberIndex.TryGetValue(fuzzyKey, out var frozenFuzzy))
                    fuzzyMemberList = frozenFuzzy;
                else if (_memberIndex.TryGetValue(fuzzyKey, out var bagFuzzy))
                    fuzzyMemberList = bagFuzzy;
                    
                if (fuzzyMemberList != null)
                {
                    foreach (var member in fuzzyMemberList)
                    {
                        var key = (member.TypeName, member.MemberName, member.MemberType, member.FilePath);
                        matchedMembers[key] = matchedMembers.GetValueOrDefault(key) + 1;
                    }
                }
            }
        }

        return matchedMembers
            .Select(kv =>
            {
                var (typeName, memberName, memberType, filePath) = kv.Key;
                var matchCount = kv.Value;
                var baseScore = FuzzyMatcher.CalculateFuzzyScore(memberName, string.Join("", keywords));
                var keywordBonus = Math.Min(matchCount - 1, 5) * 10.0;
                var score = Math.Min(baseScore + keywordBonus, 100.0);
                return (typeName, memberName, memberType, filePath, score);
            })
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.memberName.Length)
            .Take(30)
            .ToList();
    }

    public async Task<(List<(string Path, string Preview)> Results, bool Truncated)> SearchRegexAsync(
        string pattern,
        bool ignoreCase = true,
        CancellationToken ct = default,
        IProgress<double>? progress = null)
    {
        var results = new ConcurrentBag<(string Path, string Preview)>();
        var regex = new Regex(pattern, (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None) | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
        var allFiles = _frozenIndex != null 
            ? _frozenIndex.Values.SelectMany(x => x).ToList()
            : _index.Values.SelectMany(x => x).Distinct().ToList();

        int globalCount = 0;
        int processedCount = 0;
        int totalFiles = allFiles.Count;
        int truncatedFlag = 0;

        await Parallel.ForEachAsync(allFiles, ct, async (filePath, internalCt) =>
        {
            if (Interlocked.CompareExchange(ref globalCount, 0, 0) >= 100)
            {
                Interlocked.Exchange(ref truncatedFlag, 1);
                return;
            }

            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                string? line;
                int lineNum = 0;
                int matchesInThisFile = 0;

                while ((line = await reader.ReadLineAsync(internalCt)) != null)
                {
                    lineNum++;
                    if (regex.IsMatch(line))
                    {
                        var currentCount = Interlocked.Increment(ref globalCount);
                        if (currentCount <= 100)
                        {
                            results.Add((filePath, $"L{lineNum}: {line.Trim()}"));
                            matchesInThisFile++;
                        }

                        if (matchesInThisFile >= 5 || currentCount >= 100)
                        {
                            if (currentCount >= 100) Interlocked.Exchange(ref truncatedFlag, 1);
                            break;
                        }
                    }
                    if (lineNum > 20000) break;
                }
            }
            catch { }
            finally
            {
                var current = Interlocked.Increment(ref processedCount);
                if (current % 10 == 0 || current == totalFiles) progress?.Report((double)current / totalFiles);
            }
        });

        var finalCount = Interlocked.CompareExchange(ref globalCount, 0, 0);
        var wasTruncated = Interlocked.CompareExchange(ref truncatedFlag, 0, 0) == 1;
        return (results.Take(100).ToList(), wasTruncated || finalCount >= 100);
    }

    public string? GetPath(string name)
    {
        if (_frozenIndex != null && _frozenIndex.TryGetValue(name, out var frozen)) return frozen.FirstOrDefault();
        return _index.TryGetValue(name, out var paths) ? paths.FirstOrDefault() : null;
    }
    
    public IEnumerable<string> GetAllFiles()
    {
        if (_frozenIndex != null) return _frozenIndex.Values.SelectMany(x => x);
        return _index.Values.SelectMany(x => x).Distinct();
    }

    private void ResetFrozenState()
    {
        _frozenIndex = null;
        _frozenTypeMap = null;
        _frozenInheritorsMap = null;
        _frozenShortTypeMap = null;
        _frozenNgramIndex = null;
        _frozenMemberIndex = null;
        IsFrozen = false;
    }

    private static ConcurrentBag<string> ToStringBag(IEnumerable<string> values)
    {
        var list = values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new ConcurrentBag<string>(list);
    }
}
