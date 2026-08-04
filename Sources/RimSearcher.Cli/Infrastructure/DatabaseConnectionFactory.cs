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
        // 程序集版本缺失时兜底 0（编码后与无版本标记库同值，显式拒绝防静默放行）。
        var cliVersion = EncodeVersion(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));
        if (cliVersion == 0)
        {
            Console.Error.WriteLine("Error: CLI assembly version missing — version marker 0 matches any unversioned database");
            Environment.Exit(ExitCodes.Error);
        }
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
    private static int EncodeVersion(Version version)
    {
        // patch > 99 时编码与下一 minor 碰撞（3.1.100 → 30200 == 3.2.0），抛异常由 CliExceptionFilter 兜底。
        if (version.Build > 99)
            throw new InvalidOperationException($"Version patch {version.Build} exceeds 99 — encoding collides with the next minor (major*10000+minor*100+build)");
        return version.Major * 10000 + version.Minor * 100 + version.Build;
    }

    private static string DecodeVersion(int encoded) =>
        $"{encoded / 10000}.{(encoded % 10000) / 100}.{encoded % 100}";
}
