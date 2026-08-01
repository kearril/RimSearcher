using System;
using ConsoleAppFramework;
using RimSearcher.Cli.Infrastructure;
using RimSearcher.Cli.Queries;

namespace RimSearcher.Cli.Commands;

internal static class SearchCommands
{
    public static void Register(ConsoleApp.ConsoleAppBuilder app, DefRepository repository, JsonOutput output)
    {
        app.Add("search", ([Argument] string keyword, string? type = null, string? mod = null, int limit = 20, bool count = false) =>
        {
            if (count)
            {
                var countResult = repository.CountSearchResults(keyword, type, mod);
                output.Write(new { count = countResult });
                if (countResult == 0)
                {
                    MaybePrefixWildcardHint(keyword);
                    Environment.ExitCode = ExitCodes.NotFound;
                }
                return;
            }
            var results = repository.Search(keyword, type, mod, limit);
            output.Write(results);
            if (results.Count == 0)
            {
                MaybePrefixWildcardHint(keyword);
                // 无结果非零退出（stdout 仍输出 []）：与 find/get 的 NotFound 契约统一。
                Environment.ExitCode = ExitCodes.NotFound;
            }
        });

        app.Add("list", (string? type = null, string? mod = null, int limit = 20, int offset = 0) =>
        {
            output.Write(repository.List(type, mod, limit, offset));
        });
    }

    /// <summary>
    /// 0 命中时提示 FTS 整词索引机制：拉丁查询缺前缀通配时，复合名（ShieldBelt）无法命中。
    /// 只陈述机制与查询形态，不给出命令——行动由调用方自行构造。
    /// </summary>
    private static void MaybePrefixWildcardHint(string keyword)
    {
        if (keyword.Contains('*') || !keyword.Any(char.IsAsciiLetter))
            return;
        Console.Error.WriteLine(
            $"Hint: 0 hits for '{keyword}'. The FTS index tokenizes whole words — " +
            $"compound names like 'ShieldBelt' need a prefix wildcard: 'shield*'");
    }
}
