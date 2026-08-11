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

        // 写入同目录临时文件（同卷），成功后才原子替换旧库：任何失败只损失临时文件，旧库保持完整。
        // journal_mode=OFF 下事务无回滚能力，半成品防护必须放在文件交换层。
        var tempPath = dbPath + ".tmp";
        Log($"Exporting Def database to: {dbPath}");

        try
        {
            ExportCore(tempPath, Log, progress);

            // 导出成功：原子替换。File.Replace 要求目标存在（同卷原子）；首次导出用 Move。
            // 平台实现：Windows 走 ReplaceFile（NTFS 原子；FAT/exFAT 不支持，抛异常由 catch 兜底）；
            // Mac/Linux（Unity Mono 6.x，System.IO 为 corefx 实现）backup=null 时走 rename()，POSIX 原子。三平台均原子。
            if (File.Exists(dbPath))
                File.Replace(tempPath, dbPath, null);
            else
                File.Move(tempPath, dbPath);

            Log($"Export finished: {dbPath} ({new FileInfo(dbPath).Length / 1024 / 1024} MB)");
        }
        catch
        {
            // 失败：清理临时文件，旧库完好。
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
    }

    /// <summary>
    /// 导出主体：写入临时文件（由 Export 负责原子替换与失败清理）。
    /// </summary>
    private static void ExportCore(string dbPath, Action<string> log, Action<int, int>? progress)
    {
        void Log(string msg) => log(msg);

        if (File.Exists(dbPath))
        {
            // 上次中断的残留临时文件（导出失败/进程被杀），旧库不受影响。
            File.Delete(dbPath);
            Log("Cleaned up stale temporary file");
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
        var seenDefKeys = new HashSet<(string DefName, string DefType)>();

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
                    totalDefs++;

                    // 重复 (def_name, def_type) 保留首现：mod 直接 append AllDefsListForReading 或运行时改名
                    // 会制造重复对，否则 UNIQUE 索引构建时中止导出（与 SQLite UNIQUE 的 BINARY 比较一致）。
                    string defName = def.defName ?? "";
                    if (!seenDefKeys.Add((defName, typeName)))
                    {
                        Log($"Skipping duplicate {typeName}/{defName} from {def.modContentPack?.Name ?? "Unknown"}; keeping earlier occurrence");
                        // 进度按枚举总数（含重复）推进：跳过项也需回调，否则尾部重复会让进度停在不满。
                        progress?.Invoke(totalDefs, estimatedTotal);
                        continue;
                    }
                    defId++;

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
                        defName,
                        typeName,
                        label,
                        description,
                        modName,
                        packageId,
                        sourceFile,
                        json);

                    var fieldTexts = new List<string>();
                    bool fieldsCapped = false;
                    try
                    {
                        fieldsCapped = DefFieldExtractor.Extract(def, defId, fieldValueInserts, nullInserts, fieldTexts);
                    }
                    catch (Exception ex)
                    {
                        // 单个 Def 字段提取失败（反射/运行时状态意外）不中断整个导出：记日志，跳过该 Def 的字段索引。
                        Log($"Field extraction failed {typeName}/{def.defName}: {ex.Message}");
                    }
                    if (fieldsCapped)
                        Log($"Field extraction capped {typeName}/{def.defName}");
                    var ftsText = SearchTextBuilder.Build(defName, label, description, fieldTexts);

                    searchWriter.Write(defId, defName, label, description, ftsText);

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

        // 版本标记：CLI 以此认证库的产出版本。事务无回滚能力（OFF 模式），半成品防护在外层原子替换。
        // PRAGMA 赋值不接受绑定参数，值由自产版本号编码而来，无注入面。
        using (var versionCommand = conn.CreateCommand())
        {
            versionCommand.CommandText =
                $"PRAGMA user_version = {EncodeVersion(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0))}";
            versionCommand.ExecuteNonQuery();
        }

        tx.Commit();
        // totalDefs 在去重检查前自增（含跳过项），日志按实际写入行数 defId 报告。
        Log($"Wrote {defId} Defs");

        conn.Close();
    }


    /// <summary>
    /// 版本号编码为 user_version 整数（major*10000+minor*100+patch，patch ≤ 99）；
    /// 与 CLI 的 DatabaseConnectionFactory.EncodeVersion 算法一致，修改时必须同步两侧。
    /// </summary>
    private static int EncodeVersion(Version version)
    {
        // patch > 99 时编码与下一 minor 碰撞（3.1.100 → 30200 == 3.2.0），抛异常由导出流程 catch 显示失败。
        if (version.Build > 99)
            throw new InvalidOperationException($"Version patch {version.Build} exceeds 99 — encoding collides with the next minor (major*10000+minor*100+build)");
        return version.Major * 10000 + version.Minor * 100 + version.Build;
    }

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
