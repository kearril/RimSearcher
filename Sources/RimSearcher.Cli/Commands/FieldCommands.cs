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
        });

        app.Add("values", ([Argument] string fieldPath, int limit = 200) =>
        {
            output.Write(repository.GetValues(fieldPath, limit));
        });
    }
}
