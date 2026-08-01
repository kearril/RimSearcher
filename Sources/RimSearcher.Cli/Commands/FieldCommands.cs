using System;
using ConsoleAppFramework;
using RimSearcher.Cli.Infrastructure;
using RimSearcher.Cli.Queries;

namespace RimSearcher.Cli.Commands;

internal static class FieldCommands
{
    public static void Register(ConsoleApp.ConsoleAppBuilder app, FieldRepository repository, JsonOutput output)
    {
        app.Add("find", ([Argument] string fieldPath, [Argument] string value, string? type = null, string? mod = null, int limit = 50) =>
        {
            var results = repository.Find(fieldPath, value, type, mod, limit);
            output.Write(results);
            if (results.Count == 0)
            {
                Console.Error.WriteLine($"Hint: no exact matches. Try fuzzy search: rimsearcher search \"{value}\"");
                if (fieldPath.Contains('.') && !fieldPath.Contains('['))
                    Console.Error.WriteLine(
                        "Hint: field paths match literally as a suffix — nested list paths need their index segment " +
                        "(e.g. 'pawnGroupMakers[0].kindDef'); or filter with 'get <def> --field <path>'");
                // 无结果非零退出（stdout 仍输出 []），脚本可用退出码区分"未找到"与"查询失败"。
                Environment.ExitCode = ExitCodes.NotFound;
            }
        });

        app.Add("fields", ([Argument] string defName, string type, int limit = 1000) =>
        {
            var result = repository.GetFields(defName, type, limit);
            output.Write(result.Values);
            if (result.IsTruncated)
                Console.Error.WriteLine($"Hint: reached limit {limit}; results may be truncated, use --limit to increase");
            // 无结果非零退出（stdout 仍输出 []）：与 find/get 的 NotFound 契约统一。
            if (result.Values.Count == 0)
                Environment.ExitCode = ExitCodes.NotFound;
        });

        app.Add("values", ([Argument] string fieldPath, string? type = null, int limit = 200) =>
        {
            var values = repository.GetValues(fieldPath, type, limit);
            output.Write(values);
            if (values.Count == 0)
            {
                if (fieldPath.Contains('.') && !fieldPath.Contains('['))
                    Console.Error.WriteLine(
                        "Hint: field paths match literally as a suffix — nested list paths need their index segment " +
                        "(e.g. 'pawnGroupMakers[0].kindDef'); or filter with 'get <def> --field <path>'");
                // 无结果非零退出（stdout 仍输出 []）：与 find/get 的 NotFound 契约统一。
                Environment.ExitCode = ExitCodes.NotFound;
            }
        });
    }
}
