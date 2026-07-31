using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;

namespace RimSearcher.DataMod.Export;

/// <summary>
/// field_values 批量写入：攒批后一次性插入并清空缓存列表。
/// </summary>
internal static class FieldValueWriter
{
    public static void Flush(SQLiteConnection connection, List<(int DefId, string FieldPath, string FieldValue)> values)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO field_values (def_id, field_path, field_value) VALUES (@did, @fp, @fv)";
        var defId = command.Parameters.Add("@did", DbType.Int32);
        var fieldPath = command.Parameters.Add("@fp", DbType.String);
        var fieldValue = command.Parameters.Add("@fv", DbType.String);

        foreach (var value in values)
        {
            defId.Value = value.DefId;
            fieldPath.Value = value.FieldPath;
            fieldValue.Value = value.FieldValue;
            command.ExecuteNonQuery();
        }

        values.Clear();
    }
}
