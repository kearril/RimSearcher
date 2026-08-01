using Microsoft.Data.Sqlite;

namespace RimSearcher.DataMod.Export;

internal static class ExportSchema
{
    public static void Create(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA encoding='UTF-8'";
        command.ExecuteNonQuery();

        command.CommandText = @"
            CREATE TABLE defs (
                id          INTEGER PRIMARY KEY,
                def_name    TEXT NOT NULL,
                def_type    TEXT NOT NULL,
                label       TEXT,
                description TEXT,
                mod_name    TEXT NOT NULL,
                package_id  TEXT,
                source_file TEXT,
                full_data   TEXT NOT NULL
            );

            CREATE UNIQUE INDEX idx_defs_name_type ON defs(def_name, def_type);
            CREATE INDEX idx_defs_type ON defs(def_type);
            CREATE INDEX idx_defs_mod ON defs(mod_name);

            CREATE TABLE field_values (
                def_id      INTEGER NOT NULL REFERENCES defs(id),
                field_path  TEXT NOT NULL,
                field_value TEXT NOT NULL
            );

            CREATE INDEX idx_fv_def_id ON field_values(def_id);
            CREATE INDEX idx_fv_path ON field_values(field_path);
            CREATE INDEX idx_fv_value ON field_values(field_value);

            CREATE VIRTUAL TABLE defs_fts USING fts5(
                def_name,
                label,
                description,
                full_text,
                tokenize='unicode61'
            );
        ";
        command.ExecuteNonQuery();
    }
}
