using System.Text;
using RimSearcher.Server.Tools;
using RimSearcher.Core;
using RimSearcher.Server;

// 强制 UTF-8 编码以确保跨平台协议传输的字符稳定性。
Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

// 劫持标准输出至标准错误，确保只有显式调用的协议流使用 STDOUT，防止协议污染。
var protocolOut = Console.Out;
Console.SetOut(Console.Error);

var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
if (!File.Exists(configPath)) configPath = "config.json";

var appConfig = AppConfig.Load(configPath);

appConfig.CsharpSourcePaths = appConfig.CsharpSourcePaths.Distinct().ToList();
appConfig.XmlSourcePaths = appConfig.XmlSourcePaths.Distinct().ToList();

if (appConfig.CsharpSourcePaths.Count == 0 && appConfig.XmlSourcePaths.Count == 0)
{
    Console.Error.WriteLine("警告: 未检测到任何源码路径配置 (config.json)。");
}

PathSecurity.Initialize(appConfig.CsharpSourcePaths.Concat(appConfig.XmlSourcePaths));

var indexer = new SourceIndexer();
var defIndexer = new DefIndexer();

foreach (var path in appConfig.CsharpSourcePaths)
{
    if (Directory.Exists(path))
    {
        Console.Error.WriteLine($"[Indexer] 正在扫描 C# 源码: {path} ...");
        indexer.Scan(path);
    }
}

foreach (var path in appConfig.XmlSourcePaths)
{
    if (Directory.Exists(path))
    {
        Console.Error.WriteLine($"[Indexer] 正在扫描 Def XML: {path} ...");
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

Console.Error.WriteLine("RimWorld MCP Server 已启动...");
await server.RunAsync();