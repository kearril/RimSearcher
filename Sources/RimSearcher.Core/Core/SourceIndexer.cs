using System.Collections.Concurrent;
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
                var inheritance = RoslynHelper.GetClassInheritanceMap(internedFile);
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
                IndexMembers(internedFile);
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

    public List<string> GetInheritors(string baseTypeName)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (_inheritorsMap.TryGetValue(baseTypeName, out var directInheritors))
        {
            foreach (var item in directInheritors) results.Add(item);
        }

        if (_shortTypeMap.TryGetValue(baseTypeName, out var fullNames))
        {
            foreach (var fullName in fullNames)
            {
                if (_inheritorsMap.TryGetValue(fullName, out var inheritors))
                {
                    foreach (var item in inheritors) results.Add(item);
                }
            }
        }

        var shortNameCandidate = baseTypeName.Contains('.') ? baseTypeName.Split('.').Last() : baseTypeName;
        if (shortNameCandidate != baseTypeName)
        {
            if (_inheritorsMap.TryGetValue(shortNameCandidate, out var shortInheritors))
            {
                foreach (var item in shortInheritors) results.Add(item);
            }
        }

        return results.ToList();
    }

    public List<(string Child, string Parent)> GetInheritanceChain(string typeName)
    {
        var chain = new List<(string Child, string Parent)>();
        
        string? current = _typeMap.ContainsKey(typeName) ? typeName : null;
        if (current == null && _shortTypeMap.TryGetValue(typeName, out var fullNames))
        {
            current = fullNames.FirstOrDefault();
        }
        
        if (current == null) return chain;

        while (_inheritanceMap.TryGetValue(current, out var parent))
        {
            if (chain.Any(x => x.Child == current)) break;
            chain.Add((current, parent));
            
            current = _typeMap.ContainsKey(parent) ? parent : null;
            if (current == null && _shortTypeMap.TryGetValue(parent, out var parentFullNames))
            {
                current = parentFullNames.FirstOrDefault();
            }
            
            if (current == null || chain.Count > 20) break;
        }
        return chain;
    }

    public List<string> GetPathsByType(string typeName)
    {
        if (_typeMap.TryGetValue(typeName, out var paths)) return paths.ToList();
        if (_shortTypeMap.TryGetValue(typeName, out var fullNames))
        {
            return fullNames.Distinct()
                .SelectMany(fn => _typeMap.TryGetValue(fn, out var p) ? p : Enumerable.Empty<string>()).ToList();
        }
        return new List<string>();
    }

    public List<string> Search(string query)
    {
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

    private void IndexMembers(string filePath)
    {
        try
        {
            var members = RoslynHelper.ExtractAllMembers(filePath);
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
        catch { }
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
                if (_ngramIndex.TryGetValue(ngram, out var namesBag))
                {
                    foreach (var name in namesBag.Distinct())
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

        foreach (var keyword in keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword) || keyword.Length < 2) continue;
            var keyLower = keyword.ToLowerInvariant();

            if (_memberIndex.TryGetValue(keyLower, out var members))
            {
                foreach (var member in members)
                {
                    var key = (member.TypeName, member.MemberName, member.MemberType, member.FilePath);
                    matchedMembers[key] = matchedMembers.GetValueOrDefault(key) + 1;
                }
            }

            var fuzzyMatches = _memberIndex.Keys.Where(k => FuzzyMatcher.CalculateFuzzyScore(k, keyLower) >= 60.0).Take(10);
            foreach (var fuzzyKey in fuzzyMatches)
            {
                if (_memberIndex.TryGetValue(fuzzyKey, out var fuzzyMembers))
                {
                    foreach (var member in fuzzyMembers)
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
        var allFiles = _index.Values.SelectMany(x => x).Distinct().ToList();

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

    public string? GetPath(string name) => _index.TryGetValue(name, out var paths) ? paths.FirstOrDefault() : null;
    public IEnumerable<string> GetAllFiles() => _index.Values.SelectMany(x => x).Distinct();
}