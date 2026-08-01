using System.Collections.Generic;
using RimSearcher.Cli.Infrastructure;
using RimSearcher.Cli.Models;
using RimSearcher.Cli.Search;

namespace RimSearcher.Cli.Queries;

internal sealed class DefRepository
{
    private readonly DatabaseConnectionFactory _connections;

    // def_type 白名单缓存：进程内首次查询后不变（CLI 只读单一 db），顺序执行无并发。
    private static readonly HashSet<string> KnownTypes = new(StringComparer.Ordinal);
    private static bool _typesLoaded;

    public DefRepository(DatabaseConnectionFactory connections)
    {
        _connections = connections;
    }

    /// <summary>
    /// def_type 是否存在于当前库（--type 参数校验用）：拼错的类型会静默返回空结果，
    /// 无法与"确实无匹配"区分，故查询前拦截。
    /// </summary>
    public bool IsKnownType(string type)
    {
        if (!_typesLoaded)
        {
            using var connection = _connections.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT DISTINCT def_type FROM defs";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                KnownTypes.Add(reader.GetString(0));
            _typesLoaded = true;
        }

        return KnownTypes.Contains(type);
    }

    /// <summary>
    /// get 未命中时的相似名候选（同类型 LIKE 模糊匹配），供错误消息指引；
    /// 大小写不敏感，type 可为 null（按全库匹配）。
    /// </summary>
    public IReadOnlyList<string> FindSimilarDefNames(string defName, string? type, int limit)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT def_name
            FROM defs
            WHERE (@type IS NULL OR def_type = @type)
              AND def_name LIKE '%' || @name || '%'
            ORDER BY def_name
            LIMIT @limit
            """;
        QueryParameters.AddFilters(command, type, null);
        command.Parameters.AddWithValue("@name", defName);
        command.Parameters.AddWithValue("@limit", limit);

        var results = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            results.Add(reader.GetString(0));
        return results;
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
        command.CommandText = $"""
            SELECT d.def_name, d.def_type, d.label, d.mod_name, d.package_id
            FROM defs d
            {BuildListFilterSql()}
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

    /// <summary>
    /// list 的过滤后总数（无视 limit/offset），供 --total 分页使用；
    /// 与 <see cref="List"/> 共享过滤条件，防 filter 漂移。
    /// </summary>
    public long CountListed(string? type, string? mod)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COUNT(*)
            FROM defs d
            {BuildListFilterSql()}
            """;
        QueryParameters.AddFilters(command, type, mod);
        return (long)command.ExecuteScalar()!;
    }

    private static string BuildListFilterSql() =>
        "WHERE (@type IS NULL OR d.def_type = @type) AND (@mod IS NULL OR d.mod_name = @mod)";

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
