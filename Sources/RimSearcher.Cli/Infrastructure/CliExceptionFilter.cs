using System;
using System.Threading;
using System.Threading.Tasks;
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
            Console.Error.WriteLine($"Error: {exception.Message}");
            Environment.ExitCode = ExitCodes.Error;
        }
    }

    private static void HandleSqliteError(SqliteException exception)
    {
        switch (exception.SqliteErrorCode)
        {
            // 固定 SQL 的 SQLITE_ERROR 有两类来源：FTS MATCH 用户表达式、
            // null 查询引用独立表（CLI 与 DataMod 捆绑发布，旧库缺表属预期）。
            // "no such table" 是 SQLite 稳定文案，据此区分缺表（重导指引）与 FTS 语法错误。
            case SqliteError when exception.Message.Contains("no such table", StringComparison.Ordinal):
                Console.Error.WriteLine(
                    $"Database error: {exception.Message} Re-export defs.db with the current DataMod (CLI and DataMod are version-locked)");
                break;
            case SqliteError:
                Console.Error.WriteLine($"FTS query syntax error: {exception.Message}");
                Console.Error.WriteLine(
                    "Hint: use 'rimsearcher find <field> <value>' or 'rimsearcher values <field>' for exact matches");
                break;
            case SqliteCorrupt or SqliteNotADatabase:
                Console.Error.WriteLine("Database file corrupted, please re-export defs.db");
                break;
            default:
                Console.Error.WriteLine($"Database error: {exception.Message}");
                break;
        }

        Environment.ExitCode = ExitCodes.Error;
    }
}
