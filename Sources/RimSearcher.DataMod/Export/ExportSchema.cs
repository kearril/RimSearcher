using Microsoft.Data.Sqlite;

namespace RimSearcher.DataMod.Export;

internal static class ExportSchema
{
    /// <summary>
    /// 建表（不含索引）：索引由 <see cref="CreateIndexes"/> 在数据全部写入后统一构建（先插后建）。
    /// </summary>
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

            CREATE TABLE field_values (
                def_id          INTEGER NOT NULL REFERENCES defs(id),
                field_path      TEXT NOT NULL,
                field_path_rev  TEXT NOT NULL,
                field_value     TEXT NOT NULL
            );

            -- null 字段的紧凑表示：路径存字典（去重），(def_id, path_id) 一行一个空字段。
            -- 与 field_values 分离：空字段不是值，复用值通道会让每个 null 行重复存储路径文本（体积翻倍）。
            CREATE TABLE field_paths (
                id   INTEGER PRIMARY KEY,
                path TEXT NOT NULL UNIQUE
            );

            CREATE TABLE null_fields (
                def_id  INTEGER NOT NULL REFERENCES defs(id),
                path_id INTEGER NOT NULL REFERENCES field_paths(id)
            );

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

    /// <summary>
    /// 统一构建全部索引：必须在数据写入完成后调用。
    /// </summary>
    public static void CreateIndexes(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE UNIQUE INDEX idx_defs_name_type ON defs(def_name, def_type);
            CREATE INDEX idx_defs_type ON defs(def_type);
            CREATE INDEX idx_defs_mod ON defs(mod_name);

            CREATE INDEX idx_fv_def_id ON field_values(def_id);
            CREATE INDEX idx_fv_path_rev ON field_values(field_path_rev);
            CREATE INDEX idx_fv_value ON field_values(field_value);

            CREATE UNIQUE INDEX idx_null_fields ON null_fields(def_id, path_id);
            -- find null 按路径反查 def 时走 path_id 前缀（联合索引主序是 def_id，反查需要独立索引）。
            CREATE INDEX idx_null_fields_path ON null_fields(path_id);
        ";
        command.ExecuteNonQuery();
    }
}
