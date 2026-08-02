using System;
using System.IO;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace RimSearcher.Cli.Infrastructure;

internal sealed class DatabaseConnectionFactory
{
    private readonly string _databasePath;

    public DatabaseConnectionFactory(string databasePath)
    {
        _databasePath = databasePath;
    }

    public SqliteConnection Open()
    {
        if (!File.Exists(_databasePath))
        {
            Console.Error.WriteLine($"Error: {_databasePath} not found");
            Environment.Exit(ExitCodes.Error);
        }

        var connection = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly");
        connection.Open();

        // 版本认证：CLI 与 DataMod 捆绑发布，只接受同版本导出的库（严格相等）。
        // 程序集版本缺失时兜底 0（编码后与任何库都不匹配，安全失败）。
        var cliVersion = EncodeVersion(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));
        int dbVersion;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA user_version";
            dbVersion = Convert.ToInt32(command.ExecuteScalar());
        }
        if (dbVersion != cliVersion)
        {
            string dbText = dbVersion == 0
                ? "an unknown version (no version marker)"
                : $"DataMod {DecodeVersion(dbVersion)}";
            Console.Error.WriteLine(
                $"Error: defs.db was exported by {dbText}, but this CLI is {DecodeVersion(cliVersion)}. " +
                "Re-export defs.db with the matching DataMod (CLI and DataMod are version-locked)");
            Environment.Exit(ExitCodes.Error);
        }

        return connection;
    }

    /// <summary>
    /// 版本号编码为 user_version 整数（major*10000+minor*100+patch，patch ≤ 99）；
    /// 与 DataMod 的 DefExporter.EncodeVersion 算法一致，修改时必须同步两侧。
    /// </summary>
    private static int EncodeVersion(Version version) =>
        version.Major * 10000 + version.Minor * 100 + version.Build;

    private static string DecodeVersion(int encoded) =>
        $"{encoded / 10000}.{(encoded % 10000) / 100}.{encoded % 100}";
}
