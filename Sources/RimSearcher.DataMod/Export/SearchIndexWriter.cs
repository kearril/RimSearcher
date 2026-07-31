using System;
using System.Data;
using System.Data.SQLite;

namespace RimSearcher.DataMod.Export;

/// <summary>
/// 预编译的 defs_fts 全文索引插入器。
/// </summary>
internal sealed class SearchIndexWriter : IDisposable
{
    private readonly SQLiteCommand _command;
    private readonly SQLiteParameter _rowId;
    private readonly SQLiteParameter _defName;
    private readonly SQLiteParameter _label;
    private readonly SQLiteParameter _description;
    private readonly SQLiteParameter _fullText;

    public SearchIndexWriter(SQLiteConnection connection)
    {
        _command = connection.CreateCommand();
        _command.CommandText = "INSERT INTO defs_fts(rowid, def_name, label, description, full_text) VALUES (@rid, @fdn, @flbl, @fdesc, @ftxt)";
        _rowId = _command.Parameters.Add("@rid", DbType.Int32);
        _defName = _command.Parameters.Add("@fdn", DbType.String);
        _label = _command.Parameters.Add("@flbl", DbType.String);
        _description = _command.Parameters.Add("@fdesc", DbType.String);
        _fullText = _command.Parameters.Add("@ftxt", DbType.String);
    }

    public void Write(int rowId, string defName, string? label, string? description, string fullText)
    {
        _rowId.Value = rowId;
        _defName.Value = defName;
        _label.Value = (object?)label ?? DBNull.Value;
        _description.Value = (object?)description ?? DBNull.Value;
        _fullText.Value = fullText;
        _command.ExecuteNonQuery();
    }

    public void Dispose() => _command.Dispose();
}
