using System;
using System.Data.SQLite;
using System.IO;
using System.Runtime.InteropServices;

namespace RimSearcher.DataMod.Export;

/// <summary>
/// 管理导出数据库的连接：打开连接、加载 FTS5 原生扩展并应用 PRAGMA 配置。
/// 初始化失败时释放连接并向上抛出，由调用方统一处理。
/// </summary>
internal static class ExportDatabase
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string fileName);

    public static SQLiteConnection Open(string databasePath, Action<string> log)
    {
        var connection = new SQLiteConnection($"Data Source={databasePath};Version=3;");
        try
        {
            connection.Open();
            LoadFtsExtension(connection, log);
            Configure(connection);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void LoadFtsExtension(SQLiteConnection connection, Action<string> log)
    {
        connection.EnableExtensions(true);
        var architecture = IntPtr.Size == 8 ? "x64" : "x86";
        var assemblyDirectory = Path.GetDirectoryName(typeof(ExportDatabase).Assembly.Location)!;
        var interopPath = Path.Combine(assemblyDirectory, architecture, "SQLite.Interop.dll");
        log($"Trying to load FTS5 extension: {interopPath} (exists={File.Exists(interopPath)})");

        var handle = LoadLibrary(interopPath);
        log($"Preload result: 0x{handle.ToInt64():X}");

        connection.LoadExtension(interopPath, "sqlite3_fts5_init");
        log("FTS5 extension loaded");
    }

    private static void Configure(SQLiteConnection connection)
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
