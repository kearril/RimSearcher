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
            
            // Normalize the path and remove trailing slashes to ensure accurate prefix matching.
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
                // 1. Exact match
                if (fullPath.Equals(root, StringComparison.OrdinalIgnoreCase)) return true;
                
                // 2. Subdirectory match (ensure D:\Source doesn't match D:\SourceCode)
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
        throw new UnauthorizedAccessException($"Access Denied: Path {requestedPath} is not within the allowed source directories.");
    }
}