using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace RimSearcher.DataMod.Reflection;

/// <summary>
/// Def 字段缓存：按游戏反序列化器（DirectXmlToObjectNew）同一规则收集字段——
/// 层级遍历（concrete → base → object，每层取 Instance|Public|NonPublic），
/// 跳过编译器生成字段（<code>&lt;</code> 前缀）与 <see cref="UnsavedAttribute"/> 标记
/// 不允许加载的运行时字段；同名冲突时最派生版本优先。
/// </summary>
internal static class PublicFieldCache
{
    private const BindingFlags FieldBindingFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly ConcurrentDictionary<Type, FieldInfo[]> Fields = new();

    public static FieldInfo[] Get(Type type) =>
        Fields.GetOrAdd(type, static currentType => BuildFieldList(currentType));

    private static FieldInfo[] BuildFieldList(Type type)
    {
        var fields = new List<FieldInfo>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        for (Type? current = type; current != null && current != typeof(object); current = current.BaseType)
        {
            foreach (var field in current.GetFields(FieldBindingFlags))
            {
                // GetFields 会带回继承的 public/protected 字段，这里只取本层声明的，
                // 基类私有字段由后续层级循环覆盖——与游戏逐层查找语义一致。
                if (field.DeclaringType != current)
                    continue;
                if (IsSkipped(field))
                    continue;
                if (!seenNames.Add(field.Name))
                    continue;
                fields.Add(field);
            }
        }

        return fields.ToArray();
    }

    private static bool IsSkipped(FieldInfo field)
    {
        if (field.Name.StartsWith("<", StringComparison.Ordinal))
            return true;

        // 委托字段是运行时函数指针（XML 无法加载），序列化其内部会产生
        // method_ptr/m_value 深链垃圾（ThinkTreeDef.wanderDestValidator 288 节点）。
        if (typeof(Delegate).IsAssignableFrom(field.FieldType))
            return true;

        var unsaved = field.GetCustomAttribute<UnsavedAttribute>();
        return unsaved != null && !unsaved.allowLoading;
    }
}
