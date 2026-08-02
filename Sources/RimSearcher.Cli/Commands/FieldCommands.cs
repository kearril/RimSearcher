using System;
using ConsoleAppFramework;
using RimSearcher.Cli.Infrastructure;
using RimSearcher.Cli.Queries;

namespace RimSearcher.Cli.Commands;

internal static class FieldCommands
{
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
                // null 查询的 0 命中通常是旧库（无 null 表）：提示重导比模糊搜索建议更准确。
                Console.Error.WriteLine(value == "null"
                    ? "Hint: no null-field matches. Re-export with the current DataMod to enable null queries (older databases have no null rows)"
                    : $"Hint: no exact matches. Try fuzzy search: rimsearcher search \"{value}\"");
                if (fieldPath.Contains('.') && !fieldPath.Contains('['))
                    Console.Error.WriteLine(
                        "Hint: field paths match literally as a suffix — nested list paths need their index segment " +
                        "(e.g. 'pawnGroupMakers[0].kindDef'); or filter with 'get <def> --field <path>'");
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
            // 无结果非零退出（stdout 仍输出 []）：与 find/get 的 NotFound 契约统一。
            if (result.Values.Count == 0)
                Environment.ExitCode = ExitCodes.NotFound;
        });

        app.Add("values", ([Argument] string fieldPath, string? type = null, int limit = 200) =>
        {
            if (TypeGuard.RejectUnknown(type, defRepository))
                return;

            var values = fieldRepository.GetValues(fieldPath, type, limit);
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
