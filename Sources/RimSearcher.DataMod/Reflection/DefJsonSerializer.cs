using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Verse;

namespace RimSearcher.DataMod.Reflection;

/// <summary>
/// 将 Def 对象序列化为 JSON 文本，供数据库 full_data 列存储。
/// 输出契约：按反射字段名原样输出（含嵌套对象、集合与字典），字段集合与游戏反序列化器一致；
/// 最大深度 100（真实数据 JSON 深度最深 29 层，哨兵仅防御病态结构），超深输出 "$truncated"，
/// 循环引用输出 "$cyclic_ref"，嵌套 Def 引用仅输出 defName，
/// 多态对象（运行时类型 ≠ 声明类型，或声明类型为抽象/接口）在对象键首输出 "$type" 运行时类型全名，
/// 非有限数（NaN/±Infinity）输出带引号字符串（RFC 8259 允许，保证 JSON 合法）。
/// </summary>
internal static class DefJsonSerializer
{
    private const int MaxDepth = 100;

    /// <summary>
    /// 序列化指定 Def 的完整 JSON 文本。
    /// </summary>
    public static string Serialize(Def def)
    {
        var builder = new StringBuilder();
        var visited = new HashSet<object>();
        SerializeValue(def, builder, visited, 0, null);
        return builder.ToString();
    }

    private static void SerializeValue(
        object? value,
        StringBuilder builder,
        HashSet<object> visited,
        int depth,
        Type? declaredType)
    {
        if (value == null)
        {
            builder.Append("null");
            return;
        }

        if (depth > MaxDepth)
        {
            builder.Append("\"$truncated\"");
            return;
        }

        Type type = value.GetType();
        if (TrySerializeSimpleValue(value, type, builder))
            return;

        // 运行时兜底：object 类型字段持有委托时（字段类型检查无法覆盖），
        // 委托是函数指针不是数据，输出 null 而非序列化其内部结构。
        if (value is Delegate)
        {
            builder.Append("null");
            return;
        }

        if (!type.IsValueType)
        {
            if (visited.Contains(value))
            {
                builder.Append("\"$cyclic_ref\"");
                return;
            }

            visited.Add(value);
        }

        try
        {
            if (depth > 0 && value is Def defReference)
            {
                AppendQuoted(builder, defReference.defName);
                return;
            }

            if (value is IList list)
            {
                SerializeList(list, builder, visited, depth, PolymorphicTypeMarker.GetDeclaredElementType(type));
                return;
            }

            if (value is IDictionary dictionary)
            {
                var (declaredKeyType, declaredValueType) = PolymorphicTypeMarker.GetDeclaredDictionaryTypes(type);
                SerializeDictionary(dictionary, builder, visited, depth, declaredKeyType, declaredValueType);
                return;
            }

            if (value is Type typeReference)
            {
                AppendQuoted(builder, typeReference.FullName ?? typeReference.Name);
                return;
            }

            if (ReflectionTraversalPolicy.IsExcludedType(type))
            {
                builder.Append("{}");
                return;
            }

            SerializeObject(value, type, builder, visited, depth, declaredType);
        }
        finally
        {
            if (!type.IsValueType)
                visited.Remove(value);
        }
    }

    private static bool TrySerializeSimpleValue(object value, Type type, StringBuilder builder)
    {
        switch (value)
        {
            case string text:
                AppendQuoted(builder, text);
                return true;
            case bool boolean:
                builder.Append(boolean ? "true" : "false");
                return true;
            case int or long or short or byte or sbyte or uint or ulong or ushort:
                builder.Append(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
                return true;
            case float single:
                AppendNonFiniteAsQuoted(single, builder);
                return true;
            case double number:
                AppendNonFiniteAsQuoted(number, builder);
                return true;
            case decimal decimalValue:
                builder.Append(decimalValue.ToString("G", CultureInfo.InvariantCulture));
                return true;
        }

        if (!type.IsEnum)
            return false;

        AppendQuoted(builder, value.ToString());
        return true;
    }

    /// <summary>
    /// 输出浮点数值；非有限数输出带引号字符串（"NaN"/"Infinity"/"-Infinity"），
    /// 保留信息且产出合法 JSON（RFC 8259：非有限数应序列化为 null 或字符串）。
    /// 数值格式：float 用 G9（RimWorld Scribe_Values.Look 保存契约实证，镜像对齐），
    /// double 用 G17（保证 round-trip）；net472 下无精度 "G" 默认 G7/G15，精度不足会失真。
    /// </summary>
    private static void AppendNonFiniteAsQuoted(float value, StringBuilder builder)
    {
        if (float.IsNaN(value)) { AppendQuoted(builder, "NaN"); return; }
        if (float.IsPositiveInfinity(value)) { AppendQuoted(builder, "Infinity"); return; }
        if (float.IsNegativeInfinity(value)) { AppendQuoted(builder, "-Infinity"); return; }
        builder.Append(value.ToString("G9", CultureInfo.InvariantCulture));
    }

    private static void AppendNonFiniteAsQuoted(double value, StringBuilder builder)
    {
        if (double.IsNaN(value)) { AppendQuoted(builder, "NaN"); return; }
        if (double.IsPositiveInfinity(value)) { AppendQuoted(builder, "Infinity"); return; }
        if (double.IsNegativeInfinity(value)) { AppendQuoted(builder, "-Infinity"); return; }
        builder.Append(value.ToString("G17", CultureInfo.InvariantCulture));
    }

    private static void SerializeList(IList list, StringBuilder builder, HashSet<object> visited, int depth, Type? declaredElementType)
    {
        builder.Append('[');
        for (int index = 0; index < list.Count; index++)
        {
            if (index > 0)
                builder.Append(',');
            SerializeValue(list[index], builder, visited, depth + 1, declaredElementType);
        }
        builder.Append(']');
    }

    private static void SerializeDictionary(IDictionary dictionary, StringBuilder builder, HashSet<object> visited, int depth, Type? declaredKeyType, Type? declaredValueType)
    {
        builder.Append('{');
        bool first = true;
        foreach (DictionaryEntry entry in dictionary)
        {
            if (!first)
                builder.Append(',');
            first = false;
            SerializeValue(entry.Key, builder, visited, depth + 1, declaredKeyType);
            builder.Append(':');
            SerializeValue(entry.Value, builder, visited, depth + 1, declaredValueType);
        }
        builder.Append('}');
    }

    private static void SerializeObject(
        object value,
        Type type,
        StringBuilder builder,
        HashSet<object> visited,
        int depth,
        Type? declaredType)
    {
        builder.Append('{');
        bool first = true;

        // 多态标记置于对象键首：位置确定，diff 基线可精确核对。
        if (PolymorphicTypeMarker.ShouldEmit(declaredType, type))
        {
            AppendQuoted(builder, PolymorphicTypeMarker.Key);
            builder.Append(':');
            AppendQuoted(builder, PolymorphicTypeMarker.GetName(type));
            first = false;
        }

        foreach (var field in PublicFieldCache.Get(type))
        {
            if (field.Name.StartsWith("<", StringComparison.Ordinal))
                continue;

            if (!first)
                builder.Append(',');
            first = false;
            AppendQuoted(builder, field.Name);
            builder.Append(':');

            try
            {
                SerializeValue(field.GetValue(value), builder, visited, depth + 1, field.FieldType);
            }
            catch
            {
                builder.Append("null");
            }
        }
        builder.Append('}');
    }

    private static void AppendQuoted(StringBuilder builder, string? value)
    {
        builder.Append('"');
        builder.Append(Escape(value));
        builder.Append('"');
    }

    private static string Escape(string? value)
    {
        if (value == null)
            return string.Empty;

        var builder = new StringBuilder(value.Length + 4);
        foreach (char character in value)
        {
            switch (character)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                default:
                    if (character < 0x20)
                        builder.Append($"\\u{(int)character:X4}");
                    else
                        builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }
}
