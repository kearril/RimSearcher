using UnityEngine;

namespace RimSearcher.DataMod.Reflection;

/// <summary>
/// 反射遍历的排除策略：与游戏反序列化器可加载面保持一致——
/// 排除 UnityEngine.Object 派生类型（材质/贴图/游戏对象等运行时资产引用）
/// 与 Microsoft./Mono. 命名空间的引用类型（框架实现细节）；
/// 值类型（Vector2/Color/RectOffset 等 Unity struct）一律放行。
/// </summary>
internal static class ReflectionTraversalPolicy
{
    private static readonly string[] ExcludedNamespacePrefixes =
    {
        "Microsoft.",
        "Mono."
    };

    /// <summary>
    /// 判断指定类型是否应排除在序列化/字段提取之外。
    /// UnityEngine.Object 派生类为运行时资产引用，不承载 XML 数据（与游戏可加载面一致）；
    /// Microsoft./Mono. 引用类型为框架实现细节；值类型永远放行。
    /// </summary>
    public static bool IsExcludedType(Type type)
    {
        if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            return true;

        if (type.IsValueType)
            return false;

        var typeNamespace = type.Namespace;
        if (typeNamespace == null)
            return false;

        foreach (var prefix in ExcludedNamespacePrefixes)
        {
            if (typeNamespace.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
