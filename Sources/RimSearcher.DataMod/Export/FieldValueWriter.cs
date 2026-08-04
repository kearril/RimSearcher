using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace RimSearcher.DataMod.Export;

/// <summary>
/// field_values 攒批写入：减少逐行插入的事务开销。
/// </summary>
internal static class FieldValueWriter
{
    public static void Flush(SqliteConnection connection, List<(int DefId, string FieldPath, string FieldValue)> values)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO field_values (def_id, field_path, field_path_rev, field_value) VALUES (@did, @fp, @fpr, @fv)";
        var defId = command.Parameters.Add("@did", SqliteType.Integer);
        var fieldPath = command.Parameters.Add("@fp", SqliteType.Text);
        var fieldPathRev = command.Parameters.Add("@fpr", SqliteType.Text);
        var fieldValue = command.Parameters.Add("@fv", SqliteType.Text);

        foreach (var value in values)
        {
            defId.Value = value.DefId;
            fieldPath.Value = value.FieldPath;
            fieldPathRev.Value = ReversePath(value.FieldPath);
            fieldValue.Value = value.FieldValue;
            command.ExecuteNonQuery();
        }

        values.Clear();
    }

    /// <summary>
    /// 路径反转（字符级）：供 field_path_rev 列使用，
    /// 使 CLI 的后缀匹配可转为反转前缀范围查询走索引。
    /// 与 CLI 的 FieldRepository.ReversePath 算法一致，修改时必须同步两侧。
    /// 字段名虽为 ASCII，但字典 key 可为任意文本：非 BMP 字符以代理对存储，
    /// 字符级反转会拆散代理对，UTF-8 编码时损坏为 U+FFFD。
    /// 当前不处理（罕见场景），算法保持与 CLI 侧一致。
    /// </summary>
    internal static string ReversePath(string path)
    {
        var chars = path.ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }
}
