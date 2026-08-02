using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace RimSearcher.DataMod.Export;

/// <summary>
/// null_fields 攒批写入：路径先去重注册进 field_paths 字典（INSERT OR IGNORE），
/// 缓存 path → id 映射后批量写 (def_id, path_id)，减少逐行插入的事务开销。
/// </summary>
internal static class NullFieldWriter
{
    public static void Flush(
        SqliteConnection connection,
        Dictionary<string, int> pathIds,
        List<(int DefId, string FieldPath)> values)
    {
        using var register = connection.CreateCommand();
        register.CommandText = "INSERT OR IGNORE INTO field_paths (path, path_rev) VALUES (@p, @pr)";
        var registerParam = register.Parameters.Add("@p", SqliteType.Text);
        var registerRevParam = register.Parameters.Add("@pr", SqliteType.Text);

        using var selectId = connection.CreateCommand();
        selectId.CommandText = "SELECT id FROM field_paths WHERE path = @p";
        var selectParam = selectId.Parameters.Add("@p", SqliteType.Text);

        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO null_fields (def_id, path_id) VALUES (@did, @pid)";
        var defId = insert.Parameters.Add("@did", SqliteType.Integer);
        var pathId = insert.Parameters.Add("@pid", SqliteType.Integer);

        foreach (var value in values)
        {
            if (!pathIds.TryGetValue(value.FieldPath, out var id))
            {
                registerParam.Value = value.FieldPath;
                registerRevParam.Value = FieldValueWriter.ReversePath(value.FieldPath);
                register.ExecuteNonQuery();
                selectParam.Value = value.FieldPath;
                using var reader = selectId.ExecuteReader();
                reader.Read();
                id = reader.GetInt32(0);
                pathIds[value.FieldPath] = id;
            }

            defId.Value = value.DefId;
            pathId.Value = id;
            insert.ExecuteNonQuery();
        }

        values.Clear();
    }
}
