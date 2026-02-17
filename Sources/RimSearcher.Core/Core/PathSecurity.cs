using System.Runtime.InteropServices;

namespace RimSearcher.Core;

public static class PathSecurity
{
    private static readonly List<string> AllowedRoots = new();
    private static bool _enabled = true;

    public static void Initialize(IEnumerable<string> paths, bool enabled = true)
    {
        _enabled = enabled;
        
        foreach (var path in paths)
        {
            if (string.IsNullOrEmpty(path)) continue;

            var resolvedPath = ResolvePath(path);
            if (resolvedPath != null && !AllowedRoots.Contains(resolvedPath, StringComparer.OrdinalIgnoreCase))
            {
                AllowedRoots.Add(resolvedPath);
            }
        }
    }

    public static bool IsPathSafe(string requestedPath)
    {
        if (!_enabled) return true;
        if (string.IsNullOrEmpty(requestedPath)) return false;

        try
        {
            var resolvedPath = ResolvePath(requestedPath);
            if (resolvedPath == null) return false;

            return AllowedRoots.Any(root =>
            {
                if (resolvedPath.Equals(root, StringComparison.OrdinalIgnoreCase)) return true;

                var rootWithSlash = root + Path.DirectorySeparatorChar;
                if (resolvedPath.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase)) return true;

                var rootWithAltSlash = root + Path.AltDirectorySeparatorChar;
                if (resolvedPath.StartsWith(rootWithAltSlash, StringComparison.OrdinalIgnoreCase)) return true;

                return false;
            });
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolvePath(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);

            if (File.Exists(fullPath))
            {
                var fileInfo = new FileInfo(fullPath);
                if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return null;
                }
                fullPath = fileInfo.FullName;
            }
            else if (Directory.Exists(fullPath))
            {
                var dirInfo = new DirectoryInfo(fullPath);
                if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return null;
                }
                fullPath = dirInfo.FullName;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                fullPath = fullPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            }

            return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return null;
        }
    }
}
