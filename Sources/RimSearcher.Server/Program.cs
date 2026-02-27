using System.Text;
using System.Diagnostics;
using RimSearcher.Server.Tools;
using RimSearcher.Core;
using RimSearcher.Server;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

var protocolOut = Console.Out;
Console.SetOut(Console.Error);

var (appConfig, configPath, isLoaded) = AppConfig.Load();
await ServerLogger.Info("Program", "Configuration source", ("path", configPath));

bool hasPaths = appConfig.CsharpSourcePaths.Count > 0 || appConfig.XmlSourcePaths.Count > 0;
var cacheDirectory = IndexCacheService.GetDefaultCacheDirectory();
var canUseCache = IndexCacheService.EnsureCacheDirectory(cacheDirectory, out var cacheInitError);
await ServerLogger.Info("Program", "Index cache directory", ("path", cacheDirectory));

if (!canUseCache)
{
    await ServerLogger.Warning("Program", "Index cache disabled", ("path", cacheDirectory), ("reason", cacheInitError ?? "unknown"));
}

if (!isLoaded)
{
    await ServerLogger.Error("Program", "Failed to load configuration", ("path", configPath), ("reason", "file missing or JSON parse error"));
}
else if (!hasPaths)
{
    await ServerLogger.Warning("Program", "No source paths defined", ("path", configPath));
}

PathSecurity.Initialize(appConfig.CsharpSourcePaths.Concat(appConfig.XmlSourcePaths), enabled: !appConfig.SkipPathSecurity);

var indexer = new SourceIndexer();
var defIndexer = new DefIndexer(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

var failedPaths = new List<string>();
var existingCsharpPaths = new List<string>();
var existingXmlPaths = new List<string>();

foreach (var path in appConfig.CsharpSourcePaths)
{
    if (Directory.Exists(path)) existingCsharpPaths.Add(path);
    else failedPaths.Add($"C# source: {path}");
}

foreach (var path in appConfig.XmlSourcePaths)
{
    if (Directory.Exists(path)) existingXmlPaths.Add(path);
    else failedPaths.Add($"XML source: {path}");
}

var totalCsharpPaths = 0;
var totalXmlPaths = 0;
var cacheLoaded = false;
var configFingerprint = IndexCacheService.ComputeConfigFingerprint(appConfig.CsharpSourcePaths, appConfig.XmlSourcePaths);

if (hasPaths && existingCsharpPaths.Count + existingXmlPaths.Count > 0)
{
    if (canUseCache && failedPaths.Count == 0)
    {
        var loadResult = IndexCacheService.TryLoad(cacheDirectory, configFingerprint);
        if (loadResult.Success && loadResult.Snapshot != null)
        {
            indexer.ImportSnapshot(loadResult.Snapshot.Source);
            defIndexer.ImportSnapshot(loadResult.Snapshot.Def);
            indexer.FreezeIndex();
            defIndexer.FreezeIndex();
            cacheLoaded = true;
            await ServerLogger.Info("Program", "Index loaded from cache");
        }
        else
        {
            await ServerLogger.Info("Program", "Cache unavailable, rebuilding index", ("reason", loadResult.Reason));
        }
    }

    if (!cacheLoaded)
    {
        var buildStopwatch = Stopwatch.StartNew();

        foreach (var path in existingCsharpPaths)
        {
            indexer.Scan(path);
            totalCsharpPaths++;
        }

        foreach (var path in existingXmlPaths)
        {
            defIndexer.Scan(path);
            indexer.Scan(path);
            totalXmlPaths++;
        }

        if (totalCsharpPaths > 0 || totalXmlPaths > 0)
        {
            indexer.FreezeIndex();
            defIndexer.FreezeIndex();
            await ServerLogger.Info("Program", "Index build completed",
                ("csPaths", totalCsharpPaths),
                ("xmlPaths", totalXmlPaths),
                ("durationMs", buildStopwatch.ElapsedMilliseconds));

            if (canUseCache && failedPaths.Count == 0)
            {
                var snapshot = new IndexCacheSnapshot
                {
                    Source = indexer.ExportSnapshot(),
                    Def = defIndexer.ExportSnapshot()
                };

                var indexedCsharpFileCount = snapshot.Source.ProcessedFiles.Count(path =>
                    path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
                var indexedXmlFileCount = snapshot.Def.ProcessedFiles.Length;

                var saveResult = IndexCacheService.Save(
                    cacheDirectory,
                    configFingerprint,
                    snapshot,
                    buildStopwatch.Elapsed,
                    indexedCsharpFileCount,
                    indexedXmlFileCount);

                if (saveResult.Success)
                {
                    await ServerLogger.Info("Program", "Index cache saved", ("path", cacheDirectory));
                }
                else
                {
                    await ServerLogger.Warning("Program", "Failed to save index cache", ("reason", saveResult.Reason));
                }
            }
        }
    }
}

if (failedPaths.Count > 0)
{
    await ServerLogger.Warning("Program", "Some configured paths are unavailable", ("count", failedPaths.Count), ("paths", string.Join("; ", failedPaths)));
}

var server = new RimSearcher.Server.RimSearcher(protocolOut);

server.RegisterTool(new ListDirectoryTool());
server.RegisterTool(new LocateTool(indexer, defIndexer));
server.RegisterTool(new InspectTool(indexer, defIndexer));
server.RegisterTool(new TraceTool(indexer));
server.RegisterTool(new ReadCodeTool(indexer));
server.RegisterTool(new SearchRegexTool(indexer));

if (isLoaded && hasPaths)
{
    await ServerLogger.Info("Program", "RimSearcher MCP server started");
}

if (appConfig.CheckUpdates)
{
    _ = Task.Run(async () => await UpdateChecker.CheckAsync());
}

await server.RunAsync();
