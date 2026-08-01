using System;
using System.Collections.Generic;

namespace RimSearcher.Cli.Infrastructure;

/// <summary>
/// 字段路径的自然排序：数字段按数值比较（genSteps[2] 排在 genSteps[10] 前），
/// 其余按字符序；数字段整体先于文本段，短前缀在前。
/// 路径索引由 DataMod 生成且无前导零，数字段位长即数值序。
/// </summary>
internal sealed class NaturalPathComparer : IComparer<string>
{
    public static readonly NaturalPathComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x == null)
            return -1;
        if (y == null)
            return 1;

        int ix = 0, iy = 0;
        while (ix < x.Length && iy < y.Length)
        {
            bool xIsDigit = char.IsAsciiDigit(x[ix]);
            bool yIsDigit = char.IsAsciiDigit(y[iy]);
            if (xIsDigit != yIsDigit)
                return xIsDigit ? -1 : 1;

            int xEnd = ix;
            while (xEnd < x.Length && char.IsAsciiDigit(x[xEnd]) == xIsDigit)
                xEnd++;
            int yEnd = iy;
            while (yEnd < y.Length && char.IsAsciiDigit(y[yEnd]) == yIsDigit)
                yEnd++;

            int cmp;
            if (xIsDigit)
            {
                // 位长比较即数值比较（无前导零）；位长相同再逐位比。
                cmp = (xEnd - ix).CompareTo(yEnd - iy);
                if (cmp == 0)
                    cmp = string.CompareOrdinal(x, ix, y, iy, xEnd - ix);
            }
            else
            {
                int common = Math.Min(xEnd - ix, yEnd - iy);
                cmp = string.CompareOrdinal(x, ix, y, iy, common);
                if (cmp == 0)
                    cmp = (xEnd - ix).CompareTo(yEnd - iy);
            }
            if (cmp != 0)
                return cmp;

            ix = xEnd;
            iy = yEnd;
        }

        return (x.Length - ix).CompareTo(y.Length - iy);
    }
}
