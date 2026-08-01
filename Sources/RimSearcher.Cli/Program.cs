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

// 内部自更新替换命令：不经 CAF 路由，不进 help（用户决策）。
// 由 update 复制的 updater 副本进程调用，在主进程退出后替换目标 exe。
if (args.Length == 4 && args[0] == "--internal-replace")
{
    Environment.Exit(RimSearcher.Cli.Maintenance.ReleaseUpdater.InternalReplace(args[1], args[2], args[3]));
}

app.Run(args);
