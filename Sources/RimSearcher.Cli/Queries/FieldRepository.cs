using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using RimSearcher.Cli.Infrastructure;
using RimSearcher.Cli.Models;

namespace RimSearcher.Cli.Queries;

internal sealed class FieldRepository
{
    private readonly DatabaseConnectionFactory _connections;

    public FieldRepository(DatabaseConnectionFactory connections)
    {
        _connections = connections;
    }

    public IReadOnlyList<FieldMatch> Find(string fieldPath, string value, string? type, string? mod, int limit)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.def_name, d.def_type, d.label, d.mod_name, d.package_id, fv.field_path, fv.field_value
            FROM defs d
            JOIN field_values fv ON d.id = fv.def_id
            WHERE fv.field_path LIKE '%' || @path ESCAPE '\'
              AND fv.field_value = @value
              AND (@type IS NULL OR d.def_type = @type)
              AND (@mod IS NULL OR d.mod_name = @mod)
            ORDER BY d.def_type, d.def_name
            LIMIT @limit
            """;
        command.Parameters.AddWithValue("@path", EscapeLikePattern(fieldPath));
        command.Parameters.AddWithValue("@value", value);
        QueryParameters.AddFilters(command, type, mod);
        command.Parameters.AddWithValue("@limit", limit);

        var results = new List<FieldMatch>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new FieldMatch(
                reader.GetString(0), reader.GetString(1), reader.ReadLabel(0, 2),
                reader.GetString(3), reader.ReadOptionalString(4),
                reader.GetString(5), reader.GetString(6)));
        }
        return results;
    }

    public FieldListResult GetFields(string defName, string type, int limit)
    {
        using var connection = _connections.Open();

        var results = new List<FieldValue>();
        using (var command = connection.CreateCommand())
        {
            // 取 2x 行窗口补偿噪声过滤消耗；上限 40000 = DataMod 单 Def 上限 20000 的 2 倍。
            int sqlLimit = Math.Min(limit * 2, 40000);
            command.CommandText = """
                SELECT fv.field_path, fv.field_value
                FROM field_values fv
                JOIN defs d ON fv.def_id = d.id
                WHERE d.def_name = @name AND d.def_type = @type
                ORDER BY fv.field_path
                LIMIT @limit
                """;
            command.Parameters.AddWithValue("@name", defName);
            command.Parameters.AddWithValue("@type", type);
            command.Parameters.AddWithValue("@limit", sqlLimit);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (results.Count >= limit)
                    break;
                var path = reader.GetString(0);
                if (IsNoiseField(path))
                    continue;
                results.Add(new FieldValue(path, reader.GetString(1)));
            }
        }

        // 精确截断检测：以过滤后的可见行总数与返回数比较。
        // 噪声行可能吃光 SQL 取行窗口，"多读一行"或"行数==limit"都会漏报或误报。
        bool isTruncated = CountVisibleRows(connection, defName, type) > results.Count;
        return new FieldListResult(results, isTruncated);
    }

    private long CountVisibleRows(SqliteConnection connection, string defName, string type)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COUNT(*)
            FROM field_values fv
            JOIN defs d ON fv.def_id = d.id
            WHERE d.def_name = @name AND d.def_type = @type
              AND {BuildNoiseFilterSql()}
            """;
        command.Parameters.AddWithValue("@name", defName);
        command.Parameters.AddWithValue("@type", type);
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>
    /// 与 <see cref="IsNoiseField"/> 同语义的 SQL 过滤条件（末段匹配）。
    /// 注意：名单与 DataMod 的 DefFieldExtractor.SkipFieldNames 内容一致，修改时必须同步两侧。
    /// </summary>
    private static string BuildNoiseFilterSql()
    {
        var conditions = new List<string>
        {
            "fv.field_path NOT GLOB 'modContentPack.*'",
            "fv.field_path NOT GLOB '*.modContentPack.*'"
        };
        foreach (var name in NoiseFieldNames)
            conditions.Add($"fv.field_path NOT LIKE '%.{name}'");
        return string.Join(" AND ", conditions);
    }

    public IReadOnlyList<string> GetValues(string fieldPath, int limit)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT fv.field_value
            FROM field_values fv
            WHERE fv.field_path LIKE '%' || @path ESCAPE '\'
            ORDER BY fv.field_value
            LIMIT @limit
            """;
        command.Parameters.AddWithValue("@path", EscapeLikePattern(fieldPath));
        command.Parameters.AddWithValue("@limit", limit);

        var values = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            values.Add(reader.GetString(0));
        return values;
    }

    /// <summary>
    /// 转义 LIKE 模式中的通配符，使用户输入的路径按字面匹配
    /// （如含下划线的字段名不会被 %/_ 误配）。与 SQL 中的 ESCAPE '\' 配对。
    /// 注意：必须先转义反斜杠本身，再转义 %/_，否则顺序颠倒会引入错误转义。
    /// </summary>
    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static bool IsNoiseField(string path)
    {
        if (path.StartsWith("modContentPack.", StringComparison.Ordinal)
            || path.Contains(".modContentPack.", StringComparison.Ordinal))
            return true;

        int lastDot = path.LastIndexOf('.');
        int lastBracket = path.LastIndexOf('[');
        int segmentStart = Math.Max(lastDot, lastBracket) + 1;
        return NoiseFieldNames.Contains(path[segmentStart..]);
    }

    // 注意：以下名单与 DataMod 的 DefFieldExtractor.SkipFieldNames 内容一致，修改时必须同步两侧。
    // 两侧语义不同：DataMod 按完整路径精确过滤，CLI 按路径末段匹配过滤。
    private static readonly HashSet<string> NoiseFieldNames = new()
    {
        "debugRandomId", "defNameHash", "generated",
        "ignoreConfigErrors", "ignoreIllegalLabelCharacterConfigError",
        "index", "shortHash"
    };
}
