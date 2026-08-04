using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Verse;

namespace RimSearcher.DataMod.Reflection;

/// <summary>
/// 遍历 Def 对象树并提取可检索的字段值，供 field_values 表与 FTS 检索文本使用。
/// 路径格式：顶层字段名、嵌套用 "." 连接、列表项用 "[i]"、字典项用 ".key"；
/// 深度上限 4（覆盖 stages[i].statOffsets[i].value 等 mod 开发高频路径，
/// 深度 4 的对象其标量叶子可达路径深度 5）、单 Def 上限 20000 条（达上限返回 true 供导出方记录），
/// 噪声字段与 modContentPack 前缀被过滤；标量一律用不变量文化格式（bool 小写）。
/// 多态对象（运行时类型 ≠ 声明类型，或声明类型为抽象/接口）输出 "&lt;path&gt;.$type" 类型行，
/// 仅入库不进 FTS 文本（类名分词会产出通用 token 污染搜索，反查由 find/values 精确面承担）。
/// </summary>
internal static class DefFieldExtractor
{
    private const int MaxDepth = 4;
    private const int MaxValuesPerDef = 20000;

    // 注意：以下名单与 CLI 的 FieldRepository.NoiseFieldNames 内容一致，修改时必须同步两侧。
    // 两侧语义不同：DataMod 按完整路径精确过滤，CLI 按路径末段匹配过滤。
    // defName/label/description 为每 def 必有的基类字段：defs 表列与 FTS 三列已覆盖，
    // 再写入 field_values 冗余 3 行/def 且 description 大文本双份存储，剔除后字段树更干净。
    private static readonly HashSet<string> SkipFieldNames = new()
    {
        "debugRandomId", "defNameHash", "generated",
        "ignoreConfigErrors", "ignoreIllegalLabelCharacterConfigError",
        "index", "shortHash",
        "defName", "label", "description"
    };

    private static readonly HashSet<string> SkipFieldPrefixes = new()
    {
        "modContentPack."
    };

    /// <summary>
    /// 提取指定 Def 的全部字段值：写入 inserts 供入库，null 字段收集到 nullInserts（独立表，避免路径重复存储），
    /// 同时收集到 allTexts 供 FTS 文本构建。
    /// 返回是否达单 Def 上限（true = 仍有字段未提取，导出方应记录日志）。
    /// </summary>
    public static bool Extract(
        Def def,
        int defId,
        List<(int DefId, string FieldPath, string FieldValue)> inserts,
        List<(int DefId, string FieldPath)> nullInserts,
        List<string> allTexts)
    {
        var visited = new HashSet<object>();
        int count = 0;
        ExtractRecursive(def, defId, string.Empty, inserts, nullInserts, allTexts, visited, 0, ref count, null);
        return count >= MaxValuesPerDef;
    }

    private static void ExtractRecursive(
        object? value,
        int defId,
        string pathPrefix,
        List<(int DefId, string FieldPath, string FieldValue)> inserts,
        List<(int DefId, string FieldPath)> nullInserts,
        List<string> allTexts,
        HashSet<object> visited,
        int depth,
        ref int count,
        Type? declaredType)
    {
        if (value == null || depth > MaxDepth || count >= MaxValuesPerDef)
            return;

        Type type = value.GetType();
        if (!type.IsValueType)
        {
            if (visited.Contains(value))
                return;
            visited.Add(value);
        }

        // 委托字段的运行时兜底（同 DefJsonSerializer）：函数指针不是数据，不提取。
        if (value is Delegate)
            return;

        try
        {
            if (value is IList list)
            {
                ExtractList(list, defId, pathPrefix, inserts, nullInserts, allTexts, visited, depth, ref count, PolymorphicTypeMarker.GetDeclaredElementType(type));
                return;
            }

            if (value is IDictionary dictionary)
            {
                var (_, declaredValueType) = PolymorphicTypeMarker.GetDeclaredDictionaryTypes(type);
                ExtractDictionary(dictionary, defId, pathPrefix, inserts, nullInserts, allTexts, visited, depth, ref count, declaredValueType);
                return;
            }

            if (ReflectionTraversalPolicy.IsExcludedType(type))
                return;

            // 多态类型行只入库不进 FTS：类名分词会产出 "power"/"shield" 等通用 token 污染搜索，
            // 反查由 find/values 的精确面承担（与 null 值不进全文的既有通道一致）。
            if (PolymorphicTypeMarker.ShouldEmit(declaredType, type))
            {
                if (!TryAddValue(defId, $"{pathPrefix}.{PolymorphicTypeMarker.Key}",
                        PolymorphicTypeMarker.GetName(type), inserts, null, ref count))
                    return;
            }

            ExtractObjectFields(value, type, defId, pathPrefix, inserts, nullInserts, allTexts, visited, depth, ref count);
        }
        finally
        {
            if (!type.IsValueType)
                visited.Remove(value);
        }
    }

    private static void ExtractList(
        IList list,
        int defId,
        string pathPrefix,
        List<(int DefId, string FieldPath, string FieldValue)> inserts,
        List<(int DefId, string FieldPath)> nullInserts,
        List<string> allTexts,
        HashSet<object> visited,
        int depth,
        ref int count,
        Type? declaredElementType)
    {
        for (int index = 0; index < list.Count && count < MaxValuesPerDef; index++)
        {
            string itemPath = string.IsNullOrEmpty(pathPrefix)
                ? $"[{index}]"
                : $"{pathPrefix}[{index}]";
            var item = list[index];

            if (item is string text)
            {
                if (!TryAddValue(defId, itemPath, text, inserts, allTexts, ref count))
                    return;
            }
            else if (item is Type itemType)
            {
                if (!TryAddValue(defId, itemPath, itemType.FullName ?? itemType.Name, inserts, allTexts, ref count))
                    return;
            }
            else if (item is ValueType valueItem)
            {
                string? scalarText = ToScalarText(valueItem);
                if (scalarText != null
                    && !TryAddValue(defId, itemPath, scalarText, inserts, allTexts, ref count))
                    return;
            }
            else if (item is Def defReference)
            {
                if (!TryAddValue(defId, itemPath, defReference.defName, inserts, allTexts, ref count))
                    return;
            }
            else if (item != null && item.GetType().IsClass)
            {
                ExtractRecursive(item, defId, itemPath, inserts, nullInserts, allTexts, visited, depth + 1, ref count, declaredElementType);
            }
        }
    }

    private static void ExtractDictionary(
        IDictionary dictionary,
        int defId,
        string pathPrefix,
        List<(int DefId, string FieldPath, string FieldValue)> inserts,
        List<(int DefId, string FieldPath)> nullInserts,
        List<string> allTexts,
        HashSet<object> visited,
        int depth,
        ref int count,
        Type? declaredValueType)
    {
        foreach (DictionaryEntry entry in dictionary)
        {
            if (count >= MaxValuesPerDef)
                return;

            string key = entry.Key == null ? string.Empty : ToScalarText(entry.Key) ?? string.Empty;
            string entryPath = string.IsNullOrEmpty(pathPrefix)
                ? key
                : $"{pathPrefix}.{key}";

            if (entry.Value is string text)
            {
                if (!TryAddValue(defId, entryPath, text, inserts, allTexts, ref count))
                    return;
            }
            else if (entry.Value is Type valueType)
            {
                if (!TryAddValue(defId, entryPath, valueType.FullName ?? valueType.Name, inserts, allTexts, ref count))
                    return;
            }
            else if (entry.Value is ValueType valueItem)
            {
                string? scalarText = ToScalarText(valueItem);
                if (scalarText != null
                    && !TryAddValue(defId, entryPath, scalarText, inserts, allTexts, ref count))
                    return;
            }
            else if (entry.Value is Def defReference)
            {
                if (!TryAddValue(defId, entryPath, defReference.defName, inserts, allTexts, ref count))
                    return;
            }
            else if (entry.Value != null && entry.Value.GetType().IsClass)
            {
                ExtractRecursive(entry.Value, defId, entryPath, inserts, nullInserts, allTexts, visited, depth + 1, ref count, declaredValueType);
            }
        }
    }

    private static void ExtractObjectFields(
        object value,
        Type type,
        int defId,
        string pathPrefix,
        List<(int DefId, string FieldPath, string FieldValue)> inserts,
        List<(int DefId, string FieldPath)> nullInserts,
        List<string> allTexts,
        HashSet<object> visited,
        int depth,
        ref int count)
    {
        foreach (var field in PublicFieldCache.Get(type))
        {
            if (count >= MaxValuesPerDef)
                return;
            if (field.Name.StartsWith("<", StringComparison.Ordinal))
                continue;

            string fieldPath = string.IsNullOrEmpty(pathPrefix)
                ? field.Name
                : $"{pathPrefix}.{field.Name}";

            object? fieldValue;
            try { fieldValue = field.GetValue(value); }
            catch { continue; }

            if (fieldValue == null)
            {
                // null 字段收集到独立集合：支撑 find/values 的空字段（补集）查询。
                // 不走 inserts 也不走 allTexts——空字段不是值：复用值通道会重复存储路径文本（体积翻倍），
                // 且 FTS 全文若收录 null 标记，search "null" 会命中所有含空字段的 Def。
                if (!AddNullMarker(defId, fieldPath, nullInserts, ref count))
                    return;
                continue;
            }

            if (fieldValue is string text)
            {
                if (!TryAddValue(defId, fieldPath, text, inserts, allTexts, ref count))
                    return;
            }
            else if (fieldValue is Type fieldType)
            {
                if (!TryAddValue(defId, fieldPath, fieldType.FullName ?? fieldType.Name, inserts, allTexts, ref count))
                    return;
            }
            else if (fieldValue is ValueType)
            {
                string? scalarText = ToScalarText(fieldValue);
                if (scalarText != null
                    && !TryAddValue(defId, fieldPath, scalarText, inserts, allTexts, ref count))
                    return;
            }
            else if (fieldValue is Def defReference)
            {
                if (!TryAddValue(defId, fieldPath, defReference.defName, inserts, allTexts, ref count))
                    return;
            }
            else
            {
                ExtractRecursive(fieldValue, defId, fieldPath, inserts, nullInserts, allTexts, visited, depth + 1, ref count, field.FieldType);
            }
        }
    }

    /// <summary>
    /// 标量统一格式化：bool 小写，数值与枚举用不变量文化（小数点恒为 "."）。
    /// 与 DefJsonSerializer 的 simple-value 输出规则对齐（float G9 / double G17，保证 round-trip）。
    /// </summary>
    private static string? ToScalarText(object value)
    {
        if (value is bool boolean)
            return boolean ? "true" : "false";
        if (value is float single)
            return single.ToString("G9", CultureInfo.InvariantCulture);
        if (value is double number)
            return number.ToString("G17", CultureInfo.InvariantCulture);
        if (value is IFormattable formattable)
            return formattable.ToString(null, CultureInfo.InvariantCulture);
        return value.ToString();
    }

    private static bool TryAddValue(
        int defId,
        string fieldPath,
        string fieldValue,
        List<(int DefId, string FieldPath, string FieldValue)> inserts,
        List<string>? allTexts,
        ref int count)
    {
        if (count >= MaxValuesPerDef)
            return false;
        // 空串视为无值：不产生 field_values 行，也不进 null_fields，
        // 与 null 字段（可寻址空字段，支撑 find/values 补集查询）语义区分。
        if (string.IsNullOrEmpty(fieldValue))
            return true;
        if (SkipFieldNames.Contains(fieldPath))
            return true;

        foreach (var prefix in SkipFieldPrefixes)
        {
            if (fieldPath.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        allTexts?.Add(fieldValue);
        inserts.Add((defId, fieldPath, fieldValue));
        count++;
        return true;
    }

    /// <summary>
    /// 收集 null 字段的 (defId, path) 对：路径由导出方去重注册进 field_paths 字典后写 null_fields。
    /// 过滤规则与 <see cref="TryAddValue"/> 一致（count 上限、噪声字段）。
    /// </summary>
    private static bool AddNullMarker(
        int defId,
        string fieldPath,
        List<(int DefId, string FieldPath)> nullInserts,
        ref int count)
    {
        if (count >= MaxValuesPerDef)
            return false;
        if (SkipFieldNames.Contains(fieldPath))
            return true;

        foreach (var prefix in SkipFieldPrefixes)
        {
            if (fieldPath.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        nullInserts.Add((defId, fieldPath));
        count++;
        return true;
    }
}
