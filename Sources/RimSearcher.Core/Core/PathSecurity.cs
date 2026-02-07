using System.Runtime.InteropServices;

namespace RimSearcher.Core;

public static class PathSecurity
{
    private static readonly List<string> AllowedRoots = new();

    public static void Initialize(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (string.IsNullOrEmpty(path)) continue;
            
            // 规范化路径并移除尾部斜杠，确保前缀匹配逻辑的准确性。
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!AllowedRoots.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            {
                AllowedRoots.Add(fullPath);
            }
        }
    }

    public static bool IsPathSafe(string requestedPath)
    {
        if (string.IsNullOrEmpty(requestedPath)) return false;
        
        try
        {
            var fullPath = Path.GetFullPath(requestedPath);
            
            return AllowedRoots.Any(root => 
            {
                // 1. 完全匹配
                if (fullPath.Equals(root, StringComparison.OrdinalIgnoreCase)) return true;
                
                // 2. 属于子目录 (确保 D:\Source 不会匹配 D:\SourceCode)
                var rootWithSlash = root + Path.DirectorySeparatorChar;
                if (fullPath.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase)) return true;
                
                var rootWithAltSlash = root + Path.AltDirectorySeparatorChar;
                if (fullPath.StartsWith(rootWithAltSlash, StringComparison.OrdinalIgnoreCase)) return true;
                
                return false;
            });
        }
        catch
        {
            return false;
        }
    }

    public static string ValidateAndGetPath(string requestedPath)
    {
        if (IsPathSafe(requestedPath)) return requestedPath;
        throw new UnauthorizedAccessException($"拒绝访问：路径 {requestedPath} 不在允许的源码范围内。");
    }
}