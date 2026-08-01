using System;
using Microsoft.Data.Sqlite;

namespace RimSearcher.DataMod.Export;

/// <summary>
/// 管理导出数据库的连接：打开连接并应用 PRAGMA 配置。
/// FTS5 由 e_sqlite3 原生库内置，无需运行时扩展加载。
/// 初始化失败时释放连接并向上抛出，由调用方统一处理。
/// </summary>
internal static class ExportDatabase
{
    public static SqliteConnection Open(string databasePath)
    {
        // Pooling=False：连接关闭即释放文件句柄。
        // 默认连接池会让句柄残留至池回收，导致下次导出删除文件失败（"another process"）。
        var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        try
        {
            connection.Open();
            Configure(connection);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void Configure(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
                PRAGMA journal_mode=OFF;
                PRAGMA synchronous=OFF;
                PRAGMA cache_size=-20000;
                PRAGMA mmap_size=268435456;
                PRAGMA temp_store=MEMORY;
                PRAGMA page_size=8192;
            ";
        command.ExecuteNonQuery();
    }
}
