using System.Text;
using RimSearcher.Server.Tools;
using RimSearcher.Core;
using RimSearcher.Server;

// Enforce UTF-8 encoding to ensure character stability for cross-platform protocol transmission.
Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;


var protocolOut = Console.Out;
Console.SetOut(Console.Error);

var (appConfig, configPath, isLoaded) = AppConfig.Load();
await ServerLogger.Info($"[Config] Loading configuration from: {configPath}");

bool hasPaths = appConfig.CsharpSourcePaths.Count > 0 || appConfig.XmlSourcePaths.Count > 0;

if (!isLoaded)
{
    Console.Error.WriteLine($"[Error] Configuration failed (File missing or JSON error) at: {configPath}");
}
else if (!hasPaths)
{
    Console.Error.WriteLine($"[Warning] No source paths defined in config: {configPath}");
}

PathSecurity.Initialize(appConfig.CsharpSourcePaths.Concat(appConfig.XmlSourcePaths));

var indexer = new SourceIndexer();
var defIndexer = new DefIndexer();

foreach (var path in appConfig.CsharpSourcePaths)
{
    if (Directory.Exists(path))
    {
        await ServerLogger.Info($"[Indexer] Scanning C# source: {path} ...");
        indexer.Scan(path);
    }
    else
    {
        await ServerLogger.Warning($"[Indexer] C# source path not found: {path}");
    }
}

foreach (var path in appConfig.XmlSourcePaths)
{
    if (Directory.Exists(path))
    {
        await ServerLogger.Info($"[Indexer] Scanning Def XML: {path} ...");
        defIndexer.Scan(path);
        indexer.Scan(path); // Also index for raw text search
    }
    else
    {
        await ServerLogger.Warning($"[Indexer] Def XML path not found: {path}");
    }
}

// Instantiate server AFTER scanning to ensure STDOUT is reserved for protocol use only after initialization.
var server = new RimSearcher.Server.RimSearcher(protocolOut);

server.RegisterTool(new ListDirectoryTool());
server.RegisterTool(new LocateTool(indexer, defIndexer));
server.RegisterTool(new InspectTool(indexer, defIndexer));
server.RegisterTool(new TraceTool(indexer));
server.RegisterTool(new ReadCodeTool());
server.RegisterTool(new SearchRegexTool(indexer));

if (isLoaded && hasPaths)
{
    Console.Error.WriteLine("RimSearcher MCP Server started...");
}

await server.RunAsync();