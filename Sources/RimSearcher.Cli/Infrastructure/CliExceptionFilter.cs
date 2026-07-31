using ConsoleAppFramework;
using Microsoft.Data.Sqlite;

namespace RimSearcher.Cli.Infrastructure;

/// <summary>
/// 命令执行异常的统一出口：错误消息写入 stderr、退出码置 1，不向用户泄漏堆栈。
/// 参数解析类错误由框架顶层处理，经重定向的 <see cref="ConsoleApp.LogError"/> 同样写入 stderr。
/// SQLite 错误按错误码分类：FTS 语法（1）、数据库损坏（11/26）、通用（其余）。
/// </summary>
internal sealed class CliExceptionFilter(ConsoleAppFilter next) : ConsoleAppFilter(next)
{
    private const int SqliteError = 1;
    private const int SqliteCorrupt = 11;
    private const int SqliteNotADatabase = 26;

    public override async Task InvokeAsync(ConsoleAppContext context, CancellationToken cancellationToken)
    {
        try
        {
            await Next.InvokeAsync(context, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 用户中断（如 Ctrl+C），保持框架默认行为，不输出错误。
        }
        catch (SqliteException exception)
        {
            HandleSqliteError(exception);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"错误: {exception.Message}");
            Environment.ExitCode = ExitCodes.Error;
        }
    }

    private static void HandleSqliteError(SqliteException exception)
    {
        switch (exception.SqliteErrorCode)
        {
            // 查询 SQL 均为固定模板，唯一接受用户输入的是 FTS MATCH 表达式，
            // 因此执行期的 SQLITE_ERROR 基本来自 FTS 语法。按错误码分类，
            // 不依赖随 SQLite 版本变动的消息文案。
            case SqliteError when IsFtsSyntaxError(exception.Message):
                Console.Error.WriteLine($"FTS 查询语法错误: {exception.Message}");
                Console.Error.WriteLine(
                    "Hint: 数值/字段值精确匹配请用 rimsearcher find <字段> <值> 或 rimsearcher values <字段>");
                break;
            case SqliteCorrupt or SqliteNotADatabase:
                Console.Error.WriteLine("数据库文件损坏，请重新导出 defs.db");
                break;
            default:
                Console.Error.WriteLine($"数据库错误: {exception.Message}");
                break;
        }

        Environment.ExitCode = ExitCodes.Error;
    }

    private static bool IsFtsSyntaxError(string message) =>
        message.Contains("fts5", StringComparison.OrdinalIgnoreCase)
        || message.Contains("syntax error", StringComparison.OrdinalIgnoreCase);
}
