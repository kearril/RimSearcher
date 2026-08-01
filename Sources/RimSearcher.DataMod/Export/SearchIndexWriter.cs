using System;
using Microsoft.Data.Sqlite;

namespace RimSearcher.DataMod.Export;

internal sealed class SearchIndexWriter : IDisposable
{
    private readonly SqliteCommand _command;
    private readonly SqliteParameter _rowId;
    private readonly SqliteParameter _defName;
    private readonly SqliteParameter _label;
    private readonly SqliteParameter _description;
    private readonly SqliteParameter _fullText;

    public SearchIndexWriter(SqliteConnection connection)
    {
        _command = connection.CreateCommand();
        _command.CommandText = "INSERT INTO defs_fts(rowid, def_name, label, description, full_text) VALUES (@rid, @fdn, @flbl, @fdesc, @ftxt)";
        _rowId = _command.Parameters.Add("@rid", SqliteType.Integer);
        _defName = _command.Parameters.Add("@fdn", SqliteType.Text);
        _label = _command.Parameters.Add("@flbl", SqliteType.Text);
        _description = _command.Parameters.Add("@fdesc", SqliteType.Text);
        _fullText = _command.Parameters.Add("@ftxt", SqliteType.Text);
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
