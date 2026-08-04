using System;
using ConsoleAppFramework;
using RimSearcher.Cli.Infrastructure;
using RimSearcher.Cli.Queries;

namespace RimSearcher.Cli.Commands;

internal static class FieldCommands
{
    private const string IndexSegmentHint =
        "Hint: field paths match literally as a suffix — nested list paths need their index segment " +
        "(e.g. 'pawnGroupMakers[0].kindDef'); or filter with 'get <def> --field <path>'";

    public static void Register(ConsoleApp.ConsoleAppBuilder app, FieldRepository fieldRepository, DefRepository defRepository, JsonOutput output)
    {
        app.Add("find", ([Argument] string fieldPath, [Argument] string value, string? type = null, string? mod = null, int limit = 50) =>
        {
            if (TypeGuard.RejectUnknown(type, defRepository))
                return;

            var results = fieldRepository.Find(fieldPath, value, type, mod, limit);
            output.Write(results);
            if (results.Count == 0)
            {
                // null 查询 0 命中 = 该路径确实无空字段（旧库缺表由全局过滤器报错，版本捆绑不降级）。
                if (value == "null")
                {
                    Console.Error.WriteLine("Hint: no null-field matches for this path suffix");
                }
                else
                {
                    // 建议的命令必须可执行：FTS 不接受命名空间点号，取末段（类名 token 形态）；
                    // 其余 FTS 运算符字符用引号短语兜底，保证 "search <值>" 不报语法错误。
                    var fuzzyValue = value;
                    var lastDot = fuzzyValue.LastIndexOf('.');
                    if (lastDot >= 0)
                        fuzzyValue = fuzzyValue[(lastDot + 1)..];
                    if (fuzzyValue.IndexOfAny(new[] { '"', '*', '^', '(', ')', ':', '-' }) >= 0)
                        fuzzyValue = $"\"{fuzzyValue}\"";
                    Console.Error.WriteLine($"Hint: no exact matches. Try fuzzy search: rimsearcher search {fuzzyValue}");
                }
                if (fieldPath.Contains('.') && !fieldPath.Contains('['))
                    Console.Error.WriteLine(IndexSegmentHint);
                // 无结果非零退出（stdout 仍输出 []），脚本可用退出码区分"未找到"与"查询失败"。
                Environment.ExitCode = ExitCodes.NotFound;
            }
        });

        app.Add("fields", ([Argument] string defName, string type, int limit = 1000, string? filter = null) =>
        {
            if (TypeGuard.RejectUnknown(type, defRepository))
                return;

            var result = fieldRepository.GetFields(defName, type, limit, filter);
            output.Write(result.Values);
            if (result.IsTruncated)
                Console.Error.WriteLine($"Hint: reached limit {limit}; results may be truncated, use --limit to increase");
            if (result.Values.Count == 0)
            {
                Console.Error.WriteLine($"Hint: no fields found for '{defName}' (type '{type}') — def may not exist, or all fields are noise-filtered");
                // 无结果非零退出（stdout 仍输出 []）：与 find/get 的 NotFound 契约统一。
                Environment.ExitCode = ExitCodes.NotFound;
            }
        });

        app.Add("values", ([Argument] string fieldPath, string? type = null, int limit = 200) =>
        {
            if (TypeGuard.RejectUnknown(type, defRepository))
                return;

            var values = fieldRepository.GetValues(fieldPath, type, limit);
            output.Write(values);
            if (values.Count == 0)
            {
                Console.Error.WriteLine(
                    $"Hint: no values for '{fieldPath}' — the path may not exist in any def, " +
                    "or --type filtered everything (try without --type)");
                if (fieldPath.Contains('.') && !fieldPath.Contains('['))
                    Console.Error.WriteLine(IndexSegmentHint);
                // 无结果非零退出（stdout 仍输出 []）：与 find/get 的 NotFound 契约统一。
                Environment.ExitCode = ExitCodes.NotFound;
            }
        });
    }
}
