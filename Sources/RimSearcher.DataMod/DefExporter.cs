using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RimSearcher.DataMod.Export;
using RimSearcher.DataMod.Reflection;
using RimSearcher.DataMod.Search;
using Verse;

namespace RimSearcher.DataMod;

public static class DefExporter
{
    private const int BatchSize = 500;

    /// <summary>
    /// 将当前加载的全部 RimWorld Def 导出为可检索的 SQLite 数据库。
    /// </summary>
    public static void Export(string dbPath, Action<string>? log = null, Action<int, int>? progress = null)
    {
        void Log(string msg) => log?.Invoke(msg);

        Log($"Exporting Def database to: {dbPath}");

        if (File.Exists(dbPath))
        {
            File.Delete(dbPath);
            Log("Deleted old database file");
        }

        using var conn = ExportDatabase.Open(dbPath);
        ExportSchema.Create(conn);
        Log("Database schema created");

        var defTypes = GenDefDatabase.AllDefTypesWithDatabases().ToList();
        Log($"Found {defTypes.Count} Def types");

        int estimatedTotal = CountDefs(defTypes);
        Log($"Estimated total: {estimatedTotal} Defs");
        progress?.Invoke(0, estimatedTotal);

        int totalDefs = 0;
        int defId = 0;
        var fieldValueInserts = new List<(int DefId, string FieldPath, string FieldValue)>();
        var nullInserts = new List<(int DefId, string FieldPath)>();
        var pathIds = new Dictionary<string, int>();

        using var tx = conn.BeginTransaction();

        using (var defWriter = new DefRecordWriter(conn))
        using (var searchWriter = new SearchIndexWriter(conn))
        {
            foreach (var defType in defTypes)
            {
                IEnumerable<Def> defs;
                try
                {
                    defs = GenDefDatabase.GetAllDefsInDatabaseForDef(defType);
                }
                catch (Exception ex)
                {
                    Log($"Skipping type {defType.Name}: {ex.Message}");
                    continue;
                }

                string typeName = defType.Name;

                foreach (var def in defs)
                {
                    defId++;
                    totalDefs++;

                    string json;
                    try
                    {
                        json = DefJsonSerializer.Serialize(def);
                    }
                    catch (Exception ex)
                    {
                        Log($"Serialization failed {typeName}/{def.defName}: {ex.Message}");
                        // 与真实空对象可区分的失败标记，供查询方识别损坏记录。
                        json = "{\"$serializeError\":true}";
                    }

                    string? label = null;
                    try { label = def.label; } catch { }
                    string? description = null;
                    try { description = def.description; } catch { }
                    string modName = def.modContentPack?.Name ?? "Unknown";
                    string? packageId = null;
                    try { packageId = def.modContentPack?.PackageId; } catch { }
                    string? sourceFile = null;
                    try { sourceFile = def.fileName; } catch { }

                    defWriter.Write(
                        defId,
                        def.defName ?? "",
                        typeName,
                        label,
                        description,
                        modName,
                        packageId,
                        sourceFile,
                        json);

                    var fieldTexts = new List<string>();
                    bool fieldsCapped = DefFieldExtractor.Extract(def, defId, fieldValueInserts, nullInserts, fieldTexts);
                    if (fieldsCapped)
                        Log($"Field extraction capped {typeName}/{def.defName}");
                    var ftsText = SearchTextBuilder.Build(def.defName, label, description, fieldTexts);

                    searchWriter.Write(defId, def.defName ?? "", label, description, ftsText);

                    if (fieldValueInserts.Count >= BatchSize)
                    {
                        FieldValueWriter.Flush(conn, fieldValueInserts);
                    }

                    if (nullInserts.Count >= BatchSize)
                    {
                        NullFieldWriter.Flush(conn, pathIds, nullInserts);
                    }

                    if (totalDefs % BatchSize == 0)
                    {
                        Log($"Processed {totalDefs} Defs...");
                    }

                    // 每个 Def 处理完更新一次进度：进度条按 Def 数平滑推进。
                    progress?.Invoke(totalDefs, estimatedTotal);
                }
            }
        }

        FieldValueWriter.Flush(conn, fieldValueInserts);
        NullFieldWriter.Flush(conn, pathIds, nullInserts);

        // 数据全部写入后统一构建索引（先插后建）。
        ExportSchema.CreateIndexes(conn);

        // 版本标记（事务内，导出失败回滚不留半成品标记）：CLI 以此认证库的产出版本。
        // PRAGMA 赋值不接受绑定参数，值由自产版本号编码而来，无注入面。
        using (var versionCommand = conn.CreateCommand())
        {
            versionCommand.CommandText =
                $"PRAGMA user_version = {EncodeVersion(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0))}";
            versionCommand.ExecuteNonQuery();
        }

        tx.Commit();
        Log($"Wrote {totalDefs} Defs");

        conn.Close();
        Log($"Export finished: {dbPath} ({new FileInfo(dbPath).Length / 1024 / 1024} MB)");
    }


    /// <summary>
    /// 版本号编码为 user_version 整数（major*10000+minor*100+patch，patch ≤ 99）；
    /// 与 CLI 的 DatabaseConnectionFactory.EncodeVersion 算法一致，修改时必须同步两侧。
    /// </summary>
    private static int EncodeVersion(Version version) =>
        version.Major * 10000 + version.Minor * 100 + version.Build;

    private static int CountDefs(IEnumerable<Type> defTypes)
    {
        int total = 0;
        foreach (var defType in defTypes)
        {
            try
            {
                total += GenDefDatabase.GetAllDefsInDatabaseForDef(defType).Count();
            }
            catch
            {
                // 第三方 Def 数据库可能枚举失败，跳过该类型并继续导出。
            }
        }

        return total;
    }
}
