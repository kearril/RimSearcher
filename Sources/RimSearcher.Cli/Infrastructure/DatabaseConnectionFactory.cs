using System;
using System.IO;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace RimSearcher.Cli.Infrastructure;

internal sealed class DatabaseConnectionFactory
{
    // 每次发布显式维护闭区间；区间内所有已发布导出版本都必须兼容。
    private const int MinDatabaseVersion = 30105; // 3.1.5
    private const int MaxDatabaseVersion = 30300; // 3.3.0

    internal static string SupportedVersions =>
        $"{DecodeVersion(MinDatabaseVersion)} through {DecodeVersion(MaxDatabaseVersion)} (inclusive)";

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

        int dbVersion;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA user_version";
            dbVersion = Convert.ToInt32(command.ExecuteScalar());
        }
        if (dbVersion < MinDatabaseVersion || dbVersion > MaxDatabaseVersion)
        {
            var cliVersion = Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);
            string dbText = dbVersion == 0
                ? "an unknown version (no version marker)"
                : $"DataMod {DecodeVersion(dbVersion)}";
            string nextStep = dbVersion == 0
                ? "Re-export defs.db with a supported DataMod to record its version."
                : dbVersion < MinDatabaseVersion
                    ? "Re-export defs.db with a supported DataMod, or use a CLI that supports this database."
                    : "Update to a CLI that supports this database.";
            Console.Error.WriteLine(
                $"Error: defs.db was exported by {dbText}, but this CLI is {cliVersion}. " +
                $"Supported DataMod export versions: {SupportedVersions}. {nextStep}");
            Environment.Exit(ExitCodes.Error);
        }

        return connection;
    }

    private static string DecodeVersion(int encoded) =>
        $"{encoded / 10000}.{(encoded % 10000) / 100}.{encoded % 100}";
}
