using System;
using System.IO;
using System.Text;
using ConsoleAppFramework;
using RimSearcher.Cli.Commands;
using RimSearcher.Cli.Infrastructure;
using RimSearcher.Cli.Queries;

Console.OutputEncoding = Encoding.UTF8;

// 框架错误输出重定向到 stderr，保证"数据走 stdout、错误走 stderr"的契约。
ConsoleApp.LogError = message => Console.Error.WriteLine(message);

string databasePath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "defs.db");
var connectionFactory = new DatabaseConnectionFactory(databasePath);
var output = new JsonOutput();
var app = ConsoleApp.Create();
app.UseFilter<CliExceptionFilter>();
var defRepository = new DefRepository(connectionFactory);
SearchCommands.Register(app, defRepository, output);
DefCommands.Register(app, defRepository, output);
FieldCommands.Register(app, new FieldRepository(connectionFactory), output);
StatisticsCommands.Register(app, new StatisticsRepository(connectionFactory), output);
MaintenanceCommands.Register(app);

// 内部自更新替换命令：由 update 复制的 updater 副本进程调用，在主进程退出后替换目标 exe。
if (args.Length == 4 && args[0] == "--internal-replace")
{
    Environment.Exit(RimSearcher.Cli.Maintenance.ReleaseUpdater.InternalReplace(args[1], args[2], args[3]));
}

// 命令集合与下方 Register 调用一一对应，新增命令时需同步。
string[] knownCommands = ["search", "list", "get", "find", "fields", "values", "types", "mods", "install", "update"];

// 帮助输出：规范入口 -h/--help（含无参数）。自控输出以附加文档指引
if (args.Length == 0 || (args.Length == 1 && (args[0] == "-h" || args[0] == "--help")))
{
    Console.WriteLine("Usage: rimsearcher <command> [options]");
    Console.WriteLine("Commands: " + string.Join(" ", knownCommands));
    Console.WriteLine("Full documentation: skills/rimsearcher/references/cli-reference.md");
    return;
}

// 未知命令契约：框架默认输出帮助到 stdout 且 exit 0，脚本无法区分"命令不存在"与"成功"。
// 选项开头（--version 等）交给框架处理。
if (args.Length > 0 && !args[0].StartsWith('-') && !knownCommands.Contains(args[0]))
{
    Console.Error.WriteLine($"Error: unknown command '{args[0]}'");
    Console.Error.WriteLine("Usage: rimsearcher <command> [options]");
    Console.Error.WriteLine("Commands: " + string.Join(" ", knownCommands));
    Environment.Exit(ExitCodes.Error);
}

app.Run(args);
