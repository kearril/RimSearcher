using System.Text.RegularExpressions;

namespace RimSearcher.Cli.Search;

/// <summary>
/// search 子串补充的查询词判定：FTS5 只认整词，单裸词查询时另做 def_name/label 包含匹配，
/// 贴合"搜 raid 出 RaidEnemy"的直觉；带 FTS 语法（*、引号、OR/NOT、短语）的查询不触发，
/// 避免包含匹配与复合语义互相干扰。
/// </summary>
internal static class SearchSubstring
{
    // 裸词 = 纯字母数字（含 CJK 表意字），无 FTS5 元字符——同时保证 LIKE 模式无通配符可注入。
    private static readonly Regex BareWord = new(@"^[\p{L}\p{N}]+$", RegexOptions.Compiled);

    // FTS5 操作符单独作裸词会解析失败（MATCH 'or' 把 or 当运算符），引号化为字面词。
    private static readonly HashSet<string> FtsOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "or", "and", "not"
    };

    public static bool IsBareWord(string keyword) => BareWord.IsMatch(keyword);

    /// <summary>
    /// 包含匹配的词长门槛：拉丁词短于 3 字符命中面过宽（"a"/"or" 会捞起大量无关名字，纯噪音）；
    /// 中文单字是语义词（"闪"），不受限——FTS5 官方 trigram 亦以 3 字符为子串下限。
    /// </summary>
    public static bool MeetsLengthThreshold(string term) =>
        ContainsCjk(term) || term.Length >= 3;

    /// <summary>FTS 侧字面化：操作符关键词加引号（"or"），其余原样。</summary>
    public static string FtsLiteral(string term) =>
        IsFtsOperator(term) ? $"\"{term}\"" : term;

    /// <summary>子串门槛也排除操作符词（"and"/"not" 长 3 字符，作子串会命中海量英文名字）。</summary>
    public static bool IsFtsOperator(string term) => FtsOperators.Contains(term);

    /// <summary>LIKE 包含模式；裸词正则保证不含 %/_/反斜杠，无需转义。</summary>
    public static string LikePattern(string term) => "%" + term + "%";

    private static bool ContainsCjk(string text) =>
        text.Any(CjkBigramExpander.IsCjkChar);
}
