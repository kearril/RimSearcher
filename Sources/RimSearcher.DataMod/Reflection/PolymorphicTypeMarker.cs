using System;

namespace RimSearcher.DataMod.Reflection;

/// <summary>
/// 多态对象类型标记：序列化器与字段提取器共用同一判定与类型名，
/// 保证 full_data 的 "$type" 键与 field_values 的 "&lt;path&gt;.$type" 行两侧一致。
/// 规则：引用类型实例，且（运行时类型 ≠ 声明类型，或声明类型为抽象/接口）时输出运行时类型全名；
/// Def 引用以 defName 为身份（两侧均在 Def 分支短路，不经过本判定）；
/// 值类型/字符串/委托/Type/null 均无标记；集合/字典为结构节点不输出标记（元素/值按声明类型判定）。
/// </summary>
internal static class PolymorphicTypeMarker
{
    /// <summary>
    /// 类型标记键名（full_data JSON 键与 field_values 路径末段共用）。
    /// 与 CLI 的 DefCommands.CollectClassFields 捕获逻辑一致，修改时必须同步两侧。
    /// </summary>
    public const string Key = "$type";

    public static bool ShouldEmit(Type? declaredType, Type runtimeType)
    {
        if (declaredType == null || runtimeType.IsValueType)
            return false;
        return runtimeType != declaredType || declaredType.IsAbstract || declaredType.IsInterface;
    }

    public static string GetName(Type runtimeType) => runtimeType.FullName ?? runtimeType.Name;

    /// <summary>
    /// 集合元素声明类型：List&lt;T&gt; 取泛型实参，数组取元素类型；
    /// 非泛型集合返回 null（无法判定声明类型，不输出标记）。
    /// </summary>
    public static Type? GetDeclaredElementType(Type collectionType)
    {
        if (collectionType.IsArray)
            return collectionType.GetElementType();
        if (collectionType.IsGenericType)
            return collectionType.GetGenericArguments()[0];
        return null;
    }

    /// <summary>
    /// 字典键/值声明类型：仅泛型字典可判定，非泛型字典返回 null。
    /// </summary>
    public static (Type? Key, Type? Value) GetDeclaredDictionaryTypes(Type dictionaryType)
    {
        if (!dictionaryType.IsGenericType)
            return (null, null);
        var arguments = dictionaryType.GetGenericArguments();
        return (arguments[0], arguments[1]);
    }
}
