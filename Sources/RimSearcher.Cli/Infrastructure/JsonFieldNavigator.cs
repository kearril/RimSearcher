using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RimSearcher.Cli.Infrastructure;

/// <summary>
/// 按字段路径（与 fields 命令同格式：a.b[0].c）导航 Def JSON。
/// 路径格式错误（exit 1 的参数错误）与导航未命中（exit 2 的未找到）
/// 区分返回，由调用方按不同契约处理。
/// </summary>
internal static class JsonFieldNavigator
{
    public enum NavigateStatus
    {
        Ok,
        MalformedPath,
        NotFound
    }

    private readonly record struct Segment(string? Name, int? Index);

    public static NavigateStatus TryNavigate(JsonElement root, string path, out JsonElement value)
    {
        value = default;
        var segments = Parse(path);
        if (segments == null)
            return NavigateStatus.MalformedPath;

        var current = root;
        foreach (var segment in segments)
        {
            if (segment.Index is int index)
            {
                if (current.ValueKind != JsonValueKind.Array || index >= current.GetArrayLength())
                    return NavigateStatus.NotFound;
                current = current[index];
            }
            else
            {
                var name = segment.Name!;
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out var next))
                    return NavigateStatus.NotFound;
                current = next;
            }
        }

        value = current;
        return NavigateStatus.Ok;
    }

    /// <summary>
    /// 解析路径为属性/索引段序列；非法格式（空段、未闭合 [、非数字索引）返回 null。
    /// 属性名与索引段均可紧随点号或直接相邻（a.b[0][1] 合法）。
    /// </summary>
    private static List<Segment>? Parse(string path)
    {
        var segments = new List<Segment>();
        int i = 0;
        while (i < path.Length)
        {
            if (path[i] == '.')
            {
                i++;
                continue;
            }
            if (path[i] == '[')
            {
                int end = path.IndexOf(']', i);
                if (end < 0)
                    return null;
                var indexText = path[(i + 1)..end];
                if (indexText.Length == 0 || !IsAllDigits(indexText))
                    return null;
                segments.Add(new Segment(null, int.Parse(indexText)));
                i = end + 1;
                continue;
            }
            int start = i;
            while (i < path.Length && path[i] != '.' && path[i] != '[')
                i++;
            segments.Add(new Segment(path[start..i], null));
        }
        return segments;
    }

    private static bool IsAllDigits(string text)
    {
        foreach (var c in text)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }
        return true;
    }
}
