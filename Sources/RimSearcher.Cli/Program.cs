using System;
using System.IO;
using System.Linq;
using System.Text;
using ConsoleAppFramework;
using RimSearcher.Cli.Commands;
using RimSearcher.Cli.Infrastructure;
using RimSearcher.Cli.Queries;

Console.OutputEncoding = Encoding.UTF8;
// stderr 默认继承系统代码页（如 CP437），hint 内插的 defName 等可能含非 ASCII；
// Console.Error 无 OutputEncoding 属性，用 StreamWriter 替换并设 AutoFlush 保证即时写出。
Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });

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
FieldCommands.Register(app, new FieldRepository(connectionFactory), defRepository, output);
StatisticsCommands.Register(app, new StatisticsRepository(connectionFactory), output);
MaintenanceCommands.Register(app);

// 命令集合与下方 Register 调用一一对应，新增命令时需同步。
string[] knownCommands = ["search", "list", "get", "find", "fields", "values", "types", "mods", "check update"];

// 帮助输出：规范入口 -h/--help（含无参数）。自控输出以附加文档指引
if (args.Length == 0 || (args.Length == 1 && (args[0] == "-h" || args[0] == "--help")))
{
    Console.WriteLine("Usage: rimsearcher <command> [options]");
    Console.WriteLine("Commands: " + string.Join(", ", knownCommands));
    Console.WriteLine("Full documentation: skills/rimsearcher/SKILL.md");
    return;
}

// 未知命令契约：框架默认输出帮助到 stdout 且 exit 0，脚本无法区分"命令不存在"与"成功"。
// 命令路径可能包含嵌套子命令；分组命令本身交给框架处理 --help。
// 选项开头（--version 等）交给框架处理。
if (args.Length > 0 && !args[0].StartsWith('-'))
{
    var commandRoot = args[0];
    var hasSubcommands = knownCommands.Any(command =>
        command.StartsWith($"{commandRoot} ", StringComparison.Ordinal));
    var commandPath = hasSubcommands && args.Length > 1 && !args[1].StartsWith('-')
        ? $"{commandRoot} {args[1]}"
        : commandRoot;
    var isKnownCommand = knownCommands.Contains(commandPath)
        || (hasSubcommands && commandPath == commandRoot);

    if (!isKnownCommand)
    {
        Console.Error.WriteLine($"Error: unknown command '{commandPath}'");
        Console.Error.WriteLine("Usage: rimsearcher <command> [options]");
        Console.Error.WriteLine("Commands: " + string.Join(", ", knownCommands));
        Environment.Exit(ExitCodes.Error);
    }
}

app.Run(args);
