using System.Text;
using RimSearcher.Server.Tools;
using RimSearcher.Core;
using RimSearcher.Server;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

var protocolOut = Console.Out;
Console.SetOut(Console.Error);

var (appConfig, configPath, isLoaded) = AppConfig.Load();
await ServerLogger.Info($"Program: Loading configuration from {configPath}");

bool hasPaths = appConfig.CsharpSourcePaths.Count > 0 || appConfig.XmlSourcePaths.Count > 0;

if (!isLoaded)
{
    await ServerLogger.Error($"Program: Failed to load configuration from {configPath} (file missing or JSON parse error)");
}
else if (!hasPaths)
{
    await ServerLogger.Warning($"Program: No source paths defined in configuration {configPath}");
}

PathSecurity.Initialize(appConfig.CsharpSourcePaths.Concat(appConfig.XmlSourcePaths), enabled: !appConfig.SkipPathSecurity);

if (appConfig.SkipPathSecurity)
{
    await ServerLogger.Info("Program: Path security checks disabled by configuration");
}

var indexer = new SourceIndexer();
var defIndexer = new DefIndexer(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

int totalCsharpPaths = 0;
int totalXmlPaths = 0;
var failedPaths = new List<string>();

foreach (var path in appConfig.CsharpSourcePaths)
{
    if (Directory.Exists(path))
    {
        indexer.Scan(path);
        totalCsharpPaths++;
    }
    else
    {
        failedPaths.Add($"C# source: {path}");
    }
}

foreach (var path in appConfig.XmlSourcePaths)
{
    if (Directory.Exists(path))
    {
        defIndexer.Scan(path);
        indexer.Scan(path);
        totalXmlPaths++;
    }
    else
    {
        failedPaths.Add($"XML source: {path}");
    }
}

if (totalCsharpPaths > 0 || totalXmlPaths > 0)
{
    indexer.FreezeIndex();
    defIndexer.FreezeIndex();
    await ServerLogger.Info($"Program: Index build completed (C# paths: {totalCsharpPaths}, XML paths: {totalXmlPaths})");
}

if (failedPaths.Count > 0)
{
    await ServerLogger.Warning($"Program: Failed to access {failedPaths.Count} configured paths");
    foreach (var failed in failedPaths)
    {
        await ServerLogger.Warning($"Program: Unavailable path: {failed}");
    }
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
    await ServerLogger.Info("Program: RimSearcher MCP server started");
}

if (appConfig.CheckUpdates)
{
    _ = Task.Run(async () => await UpdateChecker.CheckAsync());
}

await server.RunAsync();
