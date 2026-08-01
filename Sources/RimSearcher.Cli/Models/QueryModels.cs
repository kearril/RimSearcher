using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RimSearcher.Cli.Models;

internal sealed record DefSummary(
    [property: JsonPropertyName("def_name")] string DefName,
    [property: JsonPropertyName("def_type")] string DefType,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("mod_name")] string ModName,
    [property: JsonPropertyName("package_id")] string? PackageId);

internal sealed record SearchResult(
    [property: JsonPropertyName("def_name")] string DefName,
    [property: JsonPropertyName("def_type")] string DefType,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("mod_name")] string ModName,
    [property: JsonPropertyName("package_id")] string? PackageId,
    [property: JsonPropertyName("rank")] double Rank);

internal sealed record FieldMatch(
    [property: JsonPropertyName("def_name")] string DefName,
    [property: JsonPropertyName("def_type")] string DefType,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("mod_name")] string ModName,
    [property: JsonPropertyName("package_id")] string? PackageId,
    [property: JsonPropertyName("field_path")] string FieldPath,
    [property: JsonPropertyName("field_value")] string FieldValue);

internal sealed record FieldValue(
    [property: JsonPropertyName("field_path")] string FieldPath,
    [property: JsonPropertyName("field_value")] string Value)
{
    /// <summary>
    /// 引用标注：值命中 defs.def_name 时由查询层填充全部命中类型（RimWorld defName 仅类型内唯一，
    /// 跨类型重名合法且常见），非引用字段不输出（WhenWritingNull）。
    /// </summary>
    [property: JsonPropertyName("def_type")]
    public string[]? DefTypes { get; set; }
}

/// <summary>
/// fields 查询结果：可见字段列表 + 是否因 limit 或噪声过滤被截断。
/// 不直接序列化——命令层输出 <see cref="Values"/>，截断提示走 stderr。
/// </summary>
internal sealed record FieldListResult(
    IReadOnlyList<FieldValue> Values,
    bool IsTruncated);

internal sealed record TypeCount(
    [property: JsonPropertyName("def_type")] string DefType,
    [property: JsonPropertyName("count")] int Count);

internal sealed record ModCount(
    [property: JsonPropertyName("mod_name")] string ModName,
    [property: JsonPropertyName("package_id")] string? PackageId,
    [property: JsonPropertyName("def_count")] int DefCount);

internal sealed record BriefDef(
    [property: JsonPropertyName("def_name")] string DefName,
    [property: JsonPropertyName("def_type")] string DefType,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("mod_name")] string ModName,
    [property: JsonPropertyName("package_id")] string? PackageId,
    [property: JsonPropertyName("classes")] IReadOnlyList<string> Classes);
