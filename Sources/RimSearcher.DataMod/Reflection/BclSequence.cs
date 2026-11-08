using System;
using System.Collections;
using System.Collections.Generic;

namespace RimSearcher.DataMod.Reflection;

/// <summary>
/// BCL 集合按序列导出（修法 A）：HashSet&lt;T&gt;、Stack&lt;T&gt;、Queue&lt;T&gt; 等运行时集合的内部节点
/// （_buckets/_entries/_slots 等）不是数据，改为按枚举顺序复制成列表，交给既有 List 输出逻辑。
/// 序列化器与字段提取器共用本判定，保证两侧一致。
/// 判定条件（须同时满足）：实现非泛型 IEnumerable；不是 string、IList、IDictionary；
/// 运行时类型的命名空间以 System.Collections 开头。其他可枚举类型不在此处理。
/// </summary>
internal static class BclSequence
{
    private const string CollectionsNamespacePrefix = "System.Collections";

    /// <summary>
    /// 若 value 满足序列判定，返回按枚举顺序复制的可索引列表；否则返回 null。
    /// </summary>
    public static List<object?>? TryCopyToList(object value)
    {
        if (value is string || value is IList || value is IDictionary)
            return null;
        if (value is not IEnumerable sequence)
            return null;

        string? typeNamespace = value.GetType().Namespace;
        if (typeNamespace == null || !typeNamespace.StartsWith(CollectionsNamespacePrefix, StringComparison.Ordinal))
            return null;

        var items = new List<object?>();
        foreach (var item in sequence)
            items.Add(item);
        return items;
    }
}
