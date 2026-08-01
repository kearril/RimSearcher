using System.Text;

namespace RimSearcher.DataMod.Search;

internal static class CjkBigramExpander
{
    /// <summary>
    /// 将文本中的连续 CJK 字符段展开为相邻二元组，提升 FTS5 中文检索命中率。
    /// 例如 "护盾腰带" 展开为 "护盾 盾腰 腰带"。
    /// </summary>
    public static string Expand(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var result = new StringBuilder(text);
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

            if (runStart < 0)
                continue;

            int runLength = i - runStart;
            if (runLength >= 2)
            {
                result.Append(' ');
                for (int j = runStart; j < i - 1; j++)
                {
                    result.Append(text[j]);
                    result.Append(text[j + 1]);
                    result.Append(' ');
                }
            }

            runStart = -1;
        }

        return result.ToString();
    }

    // 注：CJK Extension B（U+20000 起）在 char 上不可达，代理对按两个 char 参与分段，与 CLI 侧行为一致。
    private static bool IsCjkChar(char character) =>
        character is >= '\u4E00' and <= '\u9FFF'
            or >= '\u3400' and <= '\u4DBF';
}
