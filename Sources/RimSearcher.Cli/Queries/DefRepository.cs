using System.Collections.Generic;
using RimSearcher.Cli.Infrastructure;
using RimSearcher.Cli.Models;
using RimSearcher.Cli.Search;

namespace RimSearcher.Cli.Queries;

internal sealed class DefRepository
{
    private readonly DatabaseConnectionFactory _connections;

    public DefRepository(DatabaseConnectionFactory connections)
    {
        _connections = connections;
    }

    public long CountSearchResults(string keyword, string? type, string? mod)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM defs d
            JOIN defs_fts fts ON d.id = fts.rowid
            WHERE defs_fts MATCH @kw
              AND (@type IS NULL OR d.def_type = @type)
              AND (@mod IS NULL OR d.mod_name = @mod)
            """;
        // 查询侧 CJK 大词展开：MATCH 的空格是 AND 语义，原始整段中文 token
        // 在索引中不存在（写侧只保留原文 token + 二元组），必须替换为二元组。
        command.Parameters.AddWithValue("@kw", CjkBigramExpander.ExpandForMatch(keyword));
        QueryParameters.AddFilters(command, type, mod);
        return (long)command.ExecuteScalar()!;
    }

    public IReadOnlyList<SearchResult> Search(string keyword, string? type, string? mod, int limit)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.def_name, d.def_type, d.label, d.mod_name, d.package_id, rank
            FROM defs d
            JOIN defs_fts fts ON d.id = fts.rowid
            WHERE defs_fts MATCH @kw
              AND (@type IS NULL OR d.def_type = @type)
              AND (@mod IS NULL OR d.mod_name = @mod)
            ORDER BY rank
            LIMIT @limit
            """;
        command.Parameters.AddWithValue("@kw", CjkBigramExpander.ExpandForMatch(keyword));
        QueryParameters.AddFilters(command, type, mod);
        command.Parameters.AddWithValue("@limit", limit);

        var results = new List<SearchResult>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new SearchResult(
                reader.GetString(0), reader.GetString(1), reader.ReadLabel(0, 2),
                reader.GetString(3), reader.ReadOptionalString(4), reader.GetDouble(5)));
        }
        return results;
    }

    public IReadOnlyList<DefSummary> List(string? type, string? mod, int limit, int offset)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.def_name, d.def_type, d.label, d.mod_name, d.package_id
            FROM defs d
            WHERE (@type IS NULL OR d.def_type = @type)
              AND (@mod IS NULL OR d.mod_name = @mod)
            ORDER BY d.def_type, d.def_name
            LIMIT @limit OFFSET @offset
            """;
        QueryParameters.AddFilters(command, type, mod);
        command.Parameters.AddWithValue("@limit", limit);
        command.Parameters.AddWithValue("@offset", offset);

        var results = new List<DefSummary>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new DefSummary(
                reader.GetString(0), reader.GetString(1), reader.ReadLabel(0, 2),
                reader.GetString(3), reader.ReadOptionalString(4)));
        }
        return results;
    }

    public IReadOnlyList<string> FindTypes(string defName)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT def_type FROM defs WHERE def_name = @name";
        command.Parameters.AddWithValue("@name", defName);

        var types = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            types.Add(reader.GetString(0));
        return types;
    }

    public BriefDefSource? GetBriefSource(string defName, string type)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT def_name, def_type, label, mod_name, package_id, full_data FROM defs WHERE def_name = @name AND def_type = @type";
        command.Parameters.AddWithValue("@name", defName);
        command.Parameters.AddWithValue("@type", type);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;
        return new BriefDefSource(
            reader.GetString(0), reader.GetString(1), reader.ReadLabel(0, 2),
            reader.GetString(3), reader.ReadOptionalString(4), reader.GetString(5));
    }

    public string? GetFullData(string defName, string type)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT full_data FROM defs WHERE def_name = @name AND def_type = @type";
        command.Parameters.AddWithValue("@name", defName);
        command.Parameters.AddWithValue("@type", type);
        return command.ExecuteScalar()?.ToString();
    }
}
