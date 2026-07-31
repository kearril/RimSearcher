using System;
using System.IO;
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
        return connection;
    }
}
