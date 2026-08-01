using System.Text;

namespace RimSearcher.Cli.Search;

/// <summary>
/// 查询侧 CJK 大词展开：连续 CJK 段替换为相邻二元组（与 DataMod 写侧共享字符判定，
/// 写侧为“追加”、查询侧为“替换”——MATCH 空格是 AND，保留原文 token 会使查询落空）。
/// 两侧规则修改时必须同步。
/// </summary>
internal static class CjkBigramExpander
{
    /// <summary>
    /// 将查询文本中的连续 CJK 段展开为相邻二元组（空格 AND 连接）。
    /// 例如 "粉碎机械族" → "粉碎 碎机 机械 械族"；非 CJK 文本原样返回。
    /// </summary>
    public static string ExpandForMatch(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var result = new StringBuilder(text.Length + 8);
        int runStart = -1;

        for (int i = 0; i <= text.Length; i++)
        {
            bool isCjk = i < text.Length && IsCjkChar(text[i]);
            if (isCjk)
            {
                if (runStart < 0)
                    runStart = i;
                continue;
            }

            if (runStart >= 0)
            {
                int runLength = i - runStart;
                if (runLength >= 2)
                {
                    for (int j = runStart; j < i - 1; j++)
                    {
                        result.Append(text[j]);
                        result.Append(text[j + 1]);
                        result.Append(' ');
                    }
                }
                else
                {
                    // 单字 CJK：索引侧无单字 token，恒 0 命中，原样保留。
                    result.Append(text, runStart, runLength).Append(' ');
                }

                runStart = -1;
            }

            if (i < text.Length)
                result.Append(text[i]);
        }

        return result.ToString().TrimEnd();
    }

    // 注：CJK Extension B（U+20000 起）在 char 上不可达，代理对按两个 char 参与分段，
    // 与 DataMod 侧行为一致。
    private static bool IsCjkChar(char character) =>
        character is >= '\u4E00' and <= '\u9FFF'
            or >= '\u3400' and <= '\u4DBF';
}
