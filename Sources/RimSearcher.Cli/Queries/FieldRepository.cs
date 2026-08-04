using System;
using System.Collections.Generic;
using System.Linq;
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
        // 空路径在反转前缀范围下无意义（NextBoundary 需非空），与 GetValues 同一契约。
        if (fieldPath.Length == 0)
            throw new ArgumentException("field path must not be empty");

        // null 查询走独立表（空字段不是值）；CLI 与 DataMod 捆绑发布，只兼容新导出库。
        if (string.Equals(value, "null", StringComparison.Ordinal))
            return FindNull(fieldPath, type, mod, limit);

        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.def_name, d.def_type, d.label, d.mod_name, d.package_id, fv.field_path, fv.field_value
            FROM defs d
            JOIN field_values fv ON d.id = fv.def_id
            WHERE fv.field_path_rev >= @low AND fv.field_path_rev < @high
              AND fv.field_value = @value
              AND (@type IS NULL OR d.def_type = @type)
              AND (@mod IS NULL OR d.mod_name = @mod)
            ORDER BY d.def_type, d.def_name
            LIMIT @limit
            """;
        // 反转前缀范围查询（与 GetValues 同构）：BINARY 大小写敏感，与 values 语义统一。
        var reversed = ReversePath(fieldPath);
        command.Parameters.AddWithValue("@low", reversed);
        command.Parameters.AddWithValue("@high", NextBoundary(reversed));
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

    /// <summary>
    /// null 查询：null_fields 表（空字段）∪ field_values 中真实值为 "null" 字符串的行。
    /// 两个来源均按反转前缀范围匹配（与 GetValues 同构，BINARY 大小写敏感）；
    /// 要求新导出库（无 null 表时 SQLite 直接报错，版本捆绑不降级）。
    /// </summary>
    private IReadOnlyList<FieldMatch> FindNull(string fieldPath, string? type, string? mod, int limit)
    {
        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.def_name, d.def_type, d.label, d.mod_name, d.package_id, x.path, 'null'
            FROM (
                SELECT nf.def_id, fp.path
                FROM null_fields nf
                JOIN field_paths fp ON fp.id = nf.path_id
                WHERE fp.path_rev >= @low AND fp.path_rev < @high
                UNION
                SELECT fv.def_id, fv.field_path
                FROM field_values fv
                WHERE fv.field_path_rev >= @low AND fv.field_path_rev < @high
                  AND fv.field_value = 'null'
            ) x
            JOIN defs d ON d.id = x.def_id
            WHERE (@type IS NULL OR d.def_type = @type)
              AND (@mod IS NULL OR d.mod_name = @mod)
            ORDER BY d.def_type, d.def_name
            LIMIT @limit
            """;
        var reversed = ReversePath(fieldPath);
        command.Parameters.AddWithValue("@low", reversed);
        command.Parameters.AddWithValue("@high", NextBoundary(reversed));
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

    public FieldListResult GetFields(string defName, string type, int limit, string? filter)
    {
        using var connection = _connections.Open();

        var visible = new List<FieldValue>();
        using (var command = connection.CreateCommand())
        {
            // 全量取回后在应用层过滤与排序：SQL 取行窗口会与自然排序冲突（字典序窗口 ≠ 自然序窗口）。
            // UNION ALL 空字段行：与值行同源返回，共同走噪声过滤/自然排序/--limit。
            command.CommandText = """
                SELECT fv.field_path, fv.field_value
                FROM field_values fv
                JOIN defs d ON fv.def_id = d.id
                WHERE d.def_name = @name AND d.def_type = @type
                  AND (@filter IS NULL OR fv.field_path LIKE @pattern ESCAPE '\')
                UNION ALL
                SELECT fp.path, 'null'
                FROM field_paths fp
                JOIN null_fields nf ON nf.path_id = fp.id
                JOIN defs d ON d.id = nf.def_id
                WHERE d.def_name = @name AND d.def_type = @type
                  AND (@filter IS NULL OR fp.path LIKE @pattern ESCAPE '\')
                """;
            command.Parameters.AddWithValue("@name", defName);
            command.Parameters.AddWithValue("@type", type);
            // 空 glob 归一为 null：LIKE '' 匹配空串（等价于 0 命中），而"不传"应匹配全部。
            filter = string.IsNullOrEmpty(filter) ? null : filter;
            command.Parameters.AddWithValue("@filter", (object?)filter ?? DBNull.Value);
            // SQL 引用的参数必须绑定（即使条件短路）：@pattern 恒绑，null 时条件不生效。
            command.Parameters.AddWithValue("@pattern", (object?)(filter != null ? GlobToLike(filter) : null) ?? DBNull.Value);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var path = reader.GetString(0);
                if (IsNoiseField(path))
                    continue;
                visible.Add(new FieldValue(path, reader.GetString(1)));
            }
        }

        AnnotateReferences(connection, visible);

        bool isTruncated = visible.Count > limit;
        var results = visible
            .OrderBy(v => v.FieldPath, NaturalPathComparer.Instance)
            .Take(limit)
            .ToList();
        return new FieldListResult(results, isTruncated);
    }

    /// <summary>
    /// 引用字段标注：值命中 defs.def_name 时带出其全部 def_type，供 agent 判断引用目标类型。
    /// defName 仅类型内唯一，跨类型重名合法——故保留所有命中类型而非取其一。
    /// </summary>
    private static void AnnotateReferences(SqliteConnection connection, List<FieldValue> values)
    {
        if (values.Count == 0)
            return;

        // 只查候选 def_name（值命中 defs.def_name 才有标注意义）：去重后按批 IN 走
        // idx_defs_name_type 索引，避免每次 fields 都全表扫描 defs（15,964 行）。
        var candidates = values.Select(v => v.Value).Distinct(StringComparer.Ordinal).ToList();
        var lookup = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            // SQLite 单语句变量数上限 999（默认），分批 500 留余量。
            for (int offset = 0; offset < candidates.Count; offset += 500)
            {
                var batch = candidates.GetRange(offset, Math.Min(500, candidates.Count - offset));
                var placeholders = string.Join(", ", batch.Select((_, k) => $"@p{k}"));
                command.CommandText = $"SELECT def_name, def_type FROM defs WHERE def_name IN ({placeholders})";
                command.Parameters.Clear();
                for (int k = 0; k < batch.Count; k++)
                    command.Parameters.AddWithValue($"@p{k}", batch[k]);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var name = reader.GetString(0);
                    if (!lookup.TryGetValue(name, out var types))
                        lookup[name] = types = new List<string>();
                    types.Add(reader.GetString(1));
                }
            }
        }

        foreach (var value in values)
        {
            if (lookup.TryGetValue(value.Value, out var types))
                value.DefTypes = types.Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToArray();
        }
    }

    public IReadOnlyList<string> GetValues(string fieldPath, string? type, int limit)
    {
        if (fieldPath.Length == 0)
            throw new ArgumentException("field path must not be empty");

        using var connection = _connections.Open();
        using var command = connection.CreateCommand();
        // 后缀匹配转为反转前缀范围查询：走 idx_fv_path_rev 索引；
        // BINARY 比较大小写敏感，与文档声明一致。
        // field_path_rev 列由 DataMod 导出（捆绑发布必含）。
        var reversed = ReversePath(fieldPath);
        // UNION 空字段标记："null" 与其他值一同排序、同受 LIMIT 约束。
        // null 分支与普通分支同构（path_rev 反转前缀，BINARY 大小写敏感）——LIKE 会破坏后缀精确匹配契约。
        command.CommandText = """
            SELECT v FROM (
                SELECT DISTINCT fv.field_value AS v
                FROM field_values fv
                JOIN defs d ON fv.def_id = d.id
                WHERE fv.field_path_rev >= @low AND fv.field_path_rev < @high
                  AND (@type IS NULL OR d.def_type = @type)
                UNION
                SELECT 'null' AS v
                WHERE EXISTS (
                    SELECT 1 FROM null_fields nf
                    JOIN field_paths fp ON fp.id = nf.path_id
                    JOIN defs d ON d.id = nf.def_id
                    WHERE fp.path_rev >= @low AND fp.path_rev < @high
                      AND (@type IS NULL OR d.def_type = @type)
                )
            ) ORDER BY v LIMIT @limit
            """;
        command.Parameters.AddWithValue("@low", reversed);
        command.Parameters.AddWithValue("@high", NextBoundary(reversed));
        QueryParameters.AddFilters(command, type, null);
        command.Parameters.AddWithValue("@limit", limit);

        var values = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            values.Add(reader.GetString(0));
        return values;
    }

    /// <summary>
    /// 反转路径（字符级）；与 DataMod 的 FieldValueWriter.ReversePath 算法一致，修改时必须同步两侧。
    /// 字段名本身为 ASCII，但路径可含字典 key（任意文本，CJK 等）；非 BMP 段（代理对）按字符级反转会损坏
    /// （UTF-8 编码为 U+FFFD）。当前不处理（罕见），算法保持不变。
    /// </summary>
    private static string ReversePath(string path)
    {
        var chars = path.ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }

    /// <summary>
    /// 前缀范围上界：末字符 +1（BINARY 字符串比较，[low, high) 恰含全部以 low 为前缀的值）。
    /// 路径末字符为 ASCII，无 \uFFFF 溢出。
    /// </summary>
    private static string NextBoundary(string prefix)
    {
        var chars = prefix.ToCharArray();
        chars[^1]++;
        return new string(chars);
    }

    /// <summary>
    /// 转义 LIKE 模式中的通配符，使用户输入的路径按字面匹配
    /// （如含下划线的字段名不会被 %/_ 误配）。与 SQL 中的 ESCAPE '\' 配对。
    /// 注意：必须先转义反斜杠本身，再转义 %/_，否则顺序颠倒会引入错误转义。
    /// </summary>
    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    /// <summary>
    /// glob（仅 * 通配，跨段匹配任意字符序列）转 LIKE 模式：字面转义复用
    /// <see cref="EscapeLikePattern"/>（路径字段名常含下划线，必须精确），* 替换放最后。
    /// </summary>
    private static string GlobToLike(string glob) =>
        EscapeLikePattern(glob).Replace("*", "%");

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
        "index", "shortHash",
        "defName", "label", "description"
    };
}
