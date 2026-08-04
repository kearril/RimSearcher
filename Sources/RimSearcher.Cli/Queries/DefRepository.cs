using System;
using System.Collections.Generic;
using System.Linq;
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

    public long CountSearchResults(string keyword, string? type, string? mod, bool nameOnly)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        // 并集计数：--count 语义是总命中，不受可见行 limit 影响；UNION 按 def id 去重，
        // 与结果列表的去重口径一致（子串补充仅在单裸词时启用）。
        var substringTerm = SubstringTermFor(keyword);
        var substringSql = substringTerm is null ? "" :
            $"""
            UNION
            SELECT d.id
            FROM defs d
            WHERE {SubstringMatchClause(nameOnly)}
              AND (@type IS NULL OR d.def_type = @type)
              AND (@mod IS NULL OR d.mod_name = @mod)
            """;
        command.CommandText = $"""
            SELECT COUNT(*)
            FROM (
                SELECT d.id
                FROM defs d
                JOIN defs_fts fts ON d.id = fts.rowid
                WHERE defs_fts MATCH @kw
                  AND (@type IS NULL OR d.def_type = @type)
                  AND (@mod IS NULL OR d.mod_name = @mod)
                {substringSql}
            )
            """;
        // 查询侧 CJK 大词展开：MATCH 的空格是 AND 语义，原始整段中文 token
        // 在索引中不存在（写侧只保留原文 token + 二元组），必须替换为二元组。
        command.Parameters.AddWithValue("@kw", BuildMatchExpression(keyword, nameOnly));
        if (substringTerm is not null)
            command.Parameters.AddWithValue("@pattern", SearchSubstring.LikePattern(substringTerm));
        QueryParameters.AddFilters(command, type, mod);
        return (long)command.ExecuteScalar()!;
    }

    public IReadOnlyList<SearchResult> Search(string keyword, string? type, string? mod, int limit, bool nameOnly)
    {
        using var connection = _connections.Open();
        var results = new List<SearchResult>();
        using (var command = connection.CreateCommand())
        {
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
            command.Parameters.AddWithValue("@kw", BuildMatchExpression(keyword, nameOnly));
            QueryParameters.AddFilters(command, type, mod);
            command.Parameters.AddWithValue("@limit", limit);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new SearchResult(
                    reader.GetString(0), reader.GetString(1), reader.ReadLabel(0, 2),
                    reader.GetString(3), reader.ReadOptionalString(4), reader.GetDouble(5), "token"));
            }
        }

        // 子串补充：列表未满才查——已满则补充行不可见，跳过省一次全表 LIKE 扫描。
        var substringTerm = SubstringTermFor(keyword);
        if (results.Count >= limit || substringTerm is null)
            return results;

        var tokenKeys = results.Select(r => (r.DefName, r.DefType)).ToHashSet();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                SELECT d.def_name, d.def_type, d.label, d.mod_name, d.package_id
                FROM defs d
                WHERE {SubstringMatchClause(nameOnly)}
                  AND (@type IS NULL OR d.def_type = @type)
                  AND (@mod IS NULL OR d.mod_name = @mod)
                ORDER BY d.def_type, d.def_name
                LIMIT @limit
                """;
            command.Parameters.AddWithValue("@pattern", SearchSubstring.LikePattern(substringTerm));
            QueryParameters.AddFilters(command, type, mod);
            // 多取 token 命中数作去重损耗：补充行与 FTS 命中行重叠会消耗 SQL limit 槽位，
            // 超限由下方 results.Count >= limit 的 break 截断。
            command.Parameters.AddWithValue("@limit", limit + tokenKeys.Count);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var defName = reader.GetString(0);
                var defType = reader.GetString(1);
                // 同一 def 的 FTS 命中优先保留（带相关度），补充行只填补空缺。
                if (!tokenKeys.Add((defName, defType)))
                    continue;
                results.Add(new SearchResult(
                    defName, defType, reader.ReadLabel(0, 2),
                    reader.GetString(3), reader.ReadOptionalString(4), null, "substring"));
                if (results.Count >= limit)
                    break;
            }
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

    /// <summary>
    /// 构造 MATCH 表达式：--name-only 时限定 def_name 列（FTS 列过滤），
    /// 括号包装使 OR/AND 的每个子表达式都落在列内（否则右分支会泄漏为全字段匹配）。
    /// CJK 大词展开在列过滤内同样生效（def_name 虽全英文，保持表达式语义一致）。
    /// 操作符关键词（or/and/not）作裸词时引号化为字面词——FTS5 把裸 OR 当运算符会解析失败。
    /// </summary>
    private static string BuildMatchExpression(string keyword, bool nameOnly)
    {
        var expanded = CjkBigramExpander.ExpandForMatch(SearchSubstring.FtsLiteral(keyword));
        return nameOnly ? $"def_name:({expanded})" : expanded;
    }

    /// <summary>
    /// 子串补充的启用条件：单裸词、非 FTS 操作符、达词长门槛。FTS 语法查询（*、引号、OR/NOT、短语）
    /// 语义复杂，包含匹配与之互相干扰；"and"/"not" 等操作符词作子串会命中大量英文名字，纯噪音。
    /// </summary>
    private static string? SubstringTermFor(string keyword) =>
        SearchSubstring.IsBareWord(keyword)
            && !SearchSubstring.IsFtsOperator(keyword)
            && SearchSubstring.MeetsLengthThreshold(keyword)
            ? keyword
            : null;

    /// <summary>包含匹配的列范围：--name-only 限定 def_name，其余含 label；括号保证过滤条件作用于两个分支。</summary>
    private static string SubstringMatchClause(bool nameOnly) =>
        nameOnly
            ? "(d.def_name LIKE @pattern ESCAPE '\\')"
            : "(d.def_name LIKE @pattern ESCAPE '\\' OR d.label LIKE @pattern ESCAPE '\\')";

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
