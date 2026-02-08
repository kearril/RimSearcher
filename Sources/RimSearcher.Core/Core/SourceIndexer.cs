using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace RimSearcher.Core;

public class SourceIndexer
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _index = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _typeMap =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, string> _inheritanceMap = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _inheritorsMap =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, ConcurrentBag<string>> _shortTypeMap =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, byte> _processedFiles = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _cachedAllTypeNames = new();

    public void Scan(string rootPath)
    {
        if (!Directory.Exists(rootPath)) return;
        var blacklistedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "bin", "obj", ".git", ".vs", ".idea", ".build", "temp" };

        // Use iterative file scanning instead of recursion to avoid potential stack overflow in deeply nested structures.
        var allFiles = CollectFilesIterative(rootPath, blacklistedDirs);
        var newFiles = allFiles.Where(f => _processedFiles.TryAdd(Path.GetFullPath(f), 0)).ToList();

        // Parallel processing of source code parsing to speed up indexing for large projects.
        Parallel.ForEach(newFiles, file =>
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            _index.GetOrAdd(fileName, _ => new ConcurrentBag<string>()).Add(file);

            if (file.EndsWith(".cs"))
            {
                var inheritance = RoslynHelper.GetClassInheritanceMap(file);
                foreach (var (fullName, baseType) in inheritance)
                {
                    _typeMap.GetOrAdd(fullName, _ => new ConcurrentBag<string>()).Add(file);
                    var shortName = fullName.Contains('.') ? fullName.Split('.').Last() : fullName;
                    _shortTypeMap.GetOrAdd(shortName, _ => new ConcurrentBag<string>()).Add(fullName);

                    if (!string.IsNullOrEmpty(baseType))
                    {
                        _inheritanceMap[fullName] = baseType;
                        _inheritorsMap.GetOrAdd(baseType, _ => new ConcurrentBag<string>()).Add(fullName);
                    }
                }
            }
        });

        _cachedAllTypeNames =
            _typeMap.Keys.Concat(_shortTypeMap.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
                    if (!blacklistedDirs.Contains(Path.GetFileName(dir)))
                    {
                        stack.Push(dir);
                    }
                }
            }
            catch
            {
            }
        }

        return result;
    }

    public List<string> GetInheritors(string baseTypeName)
    {
        var fullName = ResolveToFullName(baseTypeName) ?? baseTypeName;
        return _inheritorsMap.TryGetValue(fullName, out var inheritors) ? inheritors.ToList() : new List<string>();
    }

    private string? ResolveToFullName(string typeName)
    {
        if (_typeMap.ContainsKey(typeName)) return typeName;
        if (_shortTypeMap.TryGetValue(typeName, out var fullNames)) return fullNames.FirstOrDefault();
        return null;
    }

    public List<(string Child, string Parent)> GetInheritanceChain(string typeName)
    {
        var chain = new List<(string Child, string Parent)>();
        var current = ResolveToFullName(typeName);
        if (current == null) return chain;

        while (_inheritanceMap.TryGetValue(current, out var parent))
        {
            if (chain.Any(x => x.Child == current)) break;
            chain.Add((current, parent));
            current = ResolveToFullName(parent);
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

    public List<string> SearchTypes(string query) =>
        _cachedAllTypeNames.Where(k => k.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(20).ToList();

    public List<string> Search(string query) =>
        _index.Where(kv => kv.Key.Contains(query, StringComparison.OrdinalIgnoreCase)).SelectMany(kv => kv.Value)
            .ToList();

    public async Task<(List<(string Path, string Preview)> Results, bool Truncated)> SearchRegexAsync(
        string pattern,
        bool ignoreCase = true,
        CancellationToken ct = default,
        IProgress<double>? progress = null)
    {
        var results = new ConcurrentBag<(string Path, string Preview)>();
        var regex = new Regex(pattern,
            (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None) | RegexOptions.Compiled,
            TimeSpan.FromSeconds(1));
        var allFiles = _index.Values.SelectMany(x => x).Distinct().ToList();

        int globalCount = 0;
        int processedCount = 0;
        int totalFiles = allFiles.Count;
        bool truncated = false;

        await Parallel.ForEachAsync(allFiles, ct, async (filePath, internalCt) =>
        {
            if (globalCount >= 100)
            {
                truncated = true;
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
                        results.Add((filePath, $"L{lineNum}: {line.Trim()}"));
                        matchesInThisFile++;
                        Interlocked.Increment(ref globalCount);
                        if (matchesInThisFile >= 5 || globalCount >= 100) break;
                    }

                    if (lineNum > 20000) break;
                }
            }
            catch
            {
            }
            finally
            {
                var current = Interlocked.Increment(ref processedCount);
                if (current % 10 == 0 || current == totalFiles)
                {
                    progress?.Report((double)current / totalFiles);
                }
            }
        });
        return (results.Take(100).ToList(), truncated || globalCount >= 100);
    }

    public string? GetPath(string name) => _index.TryGetValue(name, out var paths) ? paths.FirstOrDefault() : null;
    public IEnumerable<string> GetAllFiles() => _index.Values.SelectMany(x => x);
}