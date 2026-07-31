using System;
using System.Data;
using System.Data.SQLite;

namespace RimSearcher.DataMod.Export;

/// <summary>
/// 预编译的 defs 表插入器：参数复用，避免每行重建命令与参数。
/// </summary>
internal sealed class DefRecordWriter : IDisposable
{
    private readonly SQLiteCommand _command;
    private readonly SQLiteParameter _id;
    private readonly SQLiteParameter _defName;
    private readonly SQLiteParameter _defType;
    private readonly SQLiteParameter _label;
    private readonly SQLiteParameter _description;
    private readonly SQLiteParameter _modName;
    private readonly SQLiteParameter _packageId;
    private readonly SQLiteParameter _sourceFile;
    private readonly SQLiteParameter _fullData;

    public DefRecordWriter(SQLiteConnection connection)
    {
        _command = connection.CreateCommand();
        _command.CommandText = @"
            INSERT INTO defs (id, def_name, def_type, label, description, mod_name, package_id, source_file, full_data)
            VALUES (@id, @dn, @dt, @lbl, @desc, @mod, @pkg, @src, @data)";
        _id = _command.Parameters.Add("@id", DbType.Int32);
        _defName = _command.Parameters.Add("@dn", DbType.String);
        _defType = _command.Parameters.Add("@dt", DbType.String);
        _label = _command.Parameters.Add("@lbl", DbType.String);
        _description = _command.Parameters.Add("@desc", DbType.String);
        _modName = _command.Parameters.Add("@mod", DbType.String);
        _packageId = _command.Parameters.Add("@pkg", DbType.String);
        _sourceFile = _command.Parameters.Add("@src", DbType.String);
        _fullData = _command.Parameters.Add("@data", DbType.String);
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
