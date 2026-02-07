using System.Text;
using RimSearcher.Server.Tools;
using RimSearcher.Core;
using RimSearcher.Server;

// Enforce UTF-8 encoding to ensure character stability for cross-platform protocol transmission.
Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

// Hijack standard output to standard error to ensure only explicit protocol streams use STDOUT, preventing protocol pollution.
var protocolOut = Console.Out;
Console.SetOut(Console.Error);

var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
if (!File.Exists(configPath)) configPath = "config.json";

var appConfig = AppConfig.Load(configPath);

appConfig.CsharpSourcePaths = appConfig.CsharpSourcePaths.Distinct().ToList();
appConfig.XmlSourcePaths = appConfig.XmlSourcePaths.Distinct().ToList();

if (appConfig.CsharpSourcePaths.Count == 0 && appConfig.XmlSourcePaths.Count == 0)
{
    Console.Error.WriteLine("Warning: No source path configuration detected (config.json).");
}

PathSecurity.Initialize(appConfig.CsharpSourcePaths.Concat(appConfig.XmlSourcePaths));

var indexer = new SourceIndexer();
var defIndexer = new DefIndexer();

foreach (var path in appConfig.CsharpSourcePaths)
{
    if (Directory.Exists(path))
    {
        Console.Error.WriteLine($"[Indexer] Scanning C# source: {path} ...");
        indexer.Scan(path);
    }
}

foreach (var path in appConfig.XmlSourcePaths)
{
    if (Directory.Exists(path))
    {
        Console.Error.WriteLine($"[Indexer] Scanning Def XML: {path} ...");
        defIndexer.Scan(path);
    }
}

var server = new RimSearcher.Server.RimSearcher(protocolOut);
server.RegisterTool(new ListDirectoryTool());
server.RegisterTool(new LocateTool(indexer, defIndexer));
server.RegisterTool(new InspectTool(indexer, defIndexer));
server.RegisterTool(new TraceTool(indexer));
server.RegisterTool(new ReadCodeTool());
server.RegisterTool(new SearchRegexTool(indexer));

Console.Error.WriteLine("RimWorld MCP Server started...");

await server.RunAsync();