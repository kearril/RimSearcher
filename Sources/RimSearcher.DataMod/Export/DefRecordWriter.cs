using System;
using Microsoft.Data.Sqlite;

namespace RimSearcher.DataMod.Export;

/// <summary>
/// 预编译的 defs 表插入器：参数复用，避免每行重建命令与参数。
/// </summary>
internal sealed class DefRecordWriter : IDisposable
{
    private readonly SqliteCommand _command;
    private readonly SqliteParameter _id;
    private readonly SqliteParameter _defName;
    private readonly SqliteParameter _defType;
    private readonly SqliteParameter _label;
    private readonly SqliteParameter _description;
    private readonly SqliteParameter _modName;
    private readonly SqliteParameter _packageId;
    private readonly SqliteParameter _sourceFile;
    private readonly SqliteParameter _fullData;

    public DefRecordWriter(SqliteConnection connection)
    {
        _command = connection.CreateCommand();
        _command.CommandText = @"
            INSERT INTO defs (id, def_name, def_type, label, description, mod_name, package_id, source_file, full_data)
            VALUES (@id, @dn, @dt, @lbl, @desc, @mod, @pkg, @src, @data)";
        _id = _command.Parameters.Add("@id", SqliteType.Integer);
        _defName = _command.Parameters.Add("@dn", SqliteType.Text);
        _defType = _command.Parameters.Add("@dt", SqliteType.Text);
        _label = _command.Parameters.Add("@lbl", SqliteType.Text);
        _description = _command.Parameters.Add("@desc", SqliteType.Text);
        _modName = _command.Parameters.Add("@mod", SqliteType.Text);
        _packageId = _command.Parameters.Add("@pkg", SqliteType.Text);
        _sourceFile = _command.Parameters.Add("@src", SqliteType.Text);
        _fullData = _command.Parameters.Add("@data", SqliteType.Text);
    }

    public void Write(
        int id,
        string defName,
        string defType,
        string? label,
        string? description,
        string modName,
        string? packageId,
        string? sourceFile,
        string fullData)
    {
        _id.Value = id;
        _defName.Value = defName;
        _defType.Value = defType;
        _label.Value = (object?)label ?? DBNull.Value;
        _description.Value = (object?)description ?? DBNull.Value;
        _modName.Value = modName;
        _packageId.Value = (object?)packageId ?? DBNull.Value;
        _sourceFile.Value = (object?)sourceFile ?? DBNull.Value;
        _fullData.Value = fullData;
        _command.ExecuteNonQuery();
    }

    public void Dispose() => _command.Dispose();
}
