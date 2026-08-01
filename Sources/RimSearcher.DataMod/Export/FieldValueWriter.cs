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
        command.CommandText = "INSERT INTO field_values (def_id, field_path, field_value) VALUES (@did, @fp, @fv)";
        var defId = command.Parameters.Add("@did", SqliteType.Integer);
        var fieldPath = command.Parameters.Add("@fp", SqliteType.Text);
        var fieldValue = command.Parameters.Add("@fv", SqliteType.Text);

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
