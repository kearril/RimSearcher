using System.Collections.Generic;
using RimSearcher.Cli.Infrastructure;
using RimSearcher.Cli.Models;

namespace RimSearcher.Cli.Queries;

internal sealed class StatisticsRepository
{
    private readonly DatabaseConnectionFactory _connections;

    public StatisticsRepository(DatabaseConnectionFactory connections)
    {
        _connections = connections;
    }

    public IReadOnlyList<TypeCount> GetTypes()
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT def_type, COUNT(*) FROM defs GROUP BY 1 ORDER BY 2 DESC";

        var results = new List<TypeCount>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(new TypeCount(reader.GetString(0), reader.GetInt32(1)));
        return results;
    }

    public IReadOnlyList<ModCount> GetMods()
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT mod_name, package_id, COUNT(*) FROM defs GROUP BY 1, 2 ORDER BY 3 DESC";

        var results = new List<ModCount>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new ModCount(
                reader.GetString(0), reader.ReadOptionalString(1), reader.GetInt32(2)));
        }
        return results;
    }
}
