using System;
using Microsoft.Data.Sqlite;

namespace RimSearcher.Cli.Queries;

internal static class QueryParameters
{
    public static void AddFilters(SqliteCommand command, string? type, string? mod)
    {
        command.Parameters.AddWithValue("@type", (object?)type ?? DBNull.Value);
        command.Parameters.AddWithValue("@mod", (object?)mod ?? DBNull.Value);
    }
}
