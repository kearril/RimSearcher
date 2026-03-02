namespace RimSearcher.Core;

public record ResolvedModPaths
{
    public string ModName { get; init; } = string.Empty;
    public string ModRootPath { get; init; } = string.Empty;
    public List<string> CsharpPaths { get; init; } = new();
    public List<string> XmlPaths { get; init; } = new();
    public List<string> PatchesPaths { get; init; } = new();
    public List<string> DetectedVersions { get; init; } = new();
    public bool IsEnabled { get; init; } = true;
    public bool RootExists { get; init; } = true;
}

public static class ModPathResolver
{
    private static readonly string[] VersionDirNames = { "1.6", "1.5", "1.4", "1.3", "1.2", "1.1" };
    private static readonly string[] DefaultCsharpDirNames = { "Source", "Sources" };
    private static readonly string[] DefaultXmlDirNames = { "Defs", "Def" };
    private static readonly string[] DefaultPatchDirNames = { "Patches", "Patch" };

    public static List<ResolvedModPaths> Resolve(IEnumerable<ModConfig> mods, string? baseDirectory = null)
    {
        var results = new List<ResolvedModPaths>();

        foreach (var mod in mods)
        {
            var resolved = ResolveMod(mod, baseDirectory);
            results.Add(resolved);
        }

        return results;
    }

    public static ResolvedModPaths ResolveMod(ModConfig mod, string? baseDirectory = null)
    {
        var resolved = new ResolvedModPaths
        {
            ModName = mod.Name,
            IsEnabled = mod.Enabled,
            ModRootPath = ResolvePath(mod.Path, baseDirectory)
        };

        if (!mod.Enabled)
        {
            return resolved;
        }

        if (!Directory.Exists(resolved.ModRootPath))
        {
            return resolved with { RootExists = false };
        }

        var detectedVersions = DetectVersionDirectories(resolved.ModRootPath);
        var (csharpPaths, xmlPaths, patchesPaths) = ResolveAllPaths(mod, resolved.ModRootPath, detectedVersions);

        return resolved with
        {
            CsharpPaths = csharpPaths,
            XmlPaths = xmlPaths,
            PatchesPaths = patchesPaths,
            DetectedVersions = detectedVersions
        };
    }

    private static List<string> DetectVersionDirectories(string modRootPath)
    {
        var versions = new List<string>();
        
        foreach (var version in VersionDirNames)
        {
            var versionPath = Path.Combine(modRootPath, version);
            if (Directory.Exists(versionPath))
            {
                versions.Add(versionPath);
            }
        }

        return versions;
    }

    private static (List<string> Csharp, List<string> Xml, List<string> Patches) ResolveAllPaths(
        ModConfig mod, 
        string modRootPath, 
        List<string> versionDirs)
    {
        var csharpPaths = new List<string>();
        var xmlPaths = new List<string>();
        var patchesPaths = new List<string>();

        if (mod.CsharpPaths.Count > 0 || mod.XmlPaths.Count > 0)
        {
            foreach (var relativePath in mod.CsharpPaths)
            {
                var fullPath = ResolvePath(relativePath, modRootPath);
                if (Directory.Exists(fullPath)) csharpPaths.Add(fullPath);
            }
            foreach (var relativePath in mod.XmlPaths)
            {
                var fullPath = ResolvePath(relativePath, modRootPath);
                if (Directory.Exists(fullPath)) xmlPaths.Add(fullPath);
            }
        }
        else if (versionDirs.Count > 0)
        {
            foreach (var versionDir in versionDirs)
            {
                CollectDefaultPaths(versionDir, csharpPaths, xmlPaths, patchesPaths);
            }
        }
        else
        {
            CollectDefaultPaths(modRootPath, csharpPaths, xmlPaths, patchesPaths);
        }

        if (versionDirs.Count > 0)
        {
            foreach (var versionDir in versionDirs)
            {
                CollectPatchPaths(versionDir, patchesPaths);
            }
        }
        else
        {
            CollectPatchPaths(modRootPath, patchesPaths);
        }

        return (csharpPaths, xmlPaths, patchesPaths);
    }

    private static void CollectDefaultPaths(string rootDir, List<string> csharpPaths, List<string> xmlPaths, List<string> patchesPaths)
    {
        foreach (var dirName in DefaultCsharpDirNames)
        {
            var fullPath = Path.Combine(rootDir, dirName);
            if (Directory.Exists(fullPath))
            {
                csharpPaths.Add(fullPath);
                break;
            }
        }

        foreach (var dirName in DefaultXmlDirNames)
        {
            var fullPath = Path.Combine(rootDir, dirName);
            if (Directory.Exists(fullPath))
            {
                xmlPaths.Add(fullPath);
                break;
            }
        }
    }

    private static void CollectPatchPaths(string rootDir, List<string> patchesPaths)
    {
        foreach (var dirName in DefaultPatchDirNames)
        {
            var fullPath = Path.Combine(rootDir, dirName);
            if (Directory.Exists(fullPath))
            {
                patchesPaths.Add(fullPath);
                break;
            }
        }
    }

    public static string ResolvePath(string rawPath, string? baseDirectory)
    {
        var expanded = Environment.ExpandEnvironmentVariables(rawPath.Trim().Trim('"'));
        
        if (Path.IsPathRooted(expanded))
        {
            return Path.GetFullPath(expanded);
        }

        if (!string.IsNullOrEmpty(baseDirectory))
        {
            var combined = Path.Combine(baseDirectory, expanded);
            if (Directory.Exists(combined) || File.Exists(combined))
            {
                return Path.GetFullPath(combined);
            }
        }

        return Path.GetFullPath(expanded);
    }
}
