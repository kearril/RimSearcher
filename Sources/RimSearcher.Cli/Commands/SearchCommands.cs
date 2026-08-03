using System;
using ConsoleAppFramework;
using RimSearcher.Cli.Infrastructure;
using RimSearcher.Cli.Queries;
using RimSearcher.Cli.Search;

namespace RimSearcher.Cli.Commands;

internal static class SearchCommands
{
    public static void Register(ConsoleApp.ConsoleAppBuilder app, DefRepository repository, JsonOutput output)
    {
        app.Add("search", ([Argument] string keyword, string? type = null, string? mod = null, int limit = 20, bool count = false, bool nameOnly = false) =>
        {
            if (TypeGuard.RejectUnknown(type, repository))
                return;

            if (count)
            {
                var countResult = repository.CountSearchResults(keyword, type, mod, nameOnly);
                output.Write(new { count = countResult });
                if (countResult == 0)
                {
                    MaybePrefixWildcardHint(keyword);
                    Environment.ExitCode = ExitCodes.NotFound;
                }
                return;
            }
            var results = repository.Search(keyword, type, mod, limit, nameOnly);
            output.Write(results);
            if (results.Count == 0)
            {
                MaybePrefixWildcardHint(keyword);
                // 无结果非零退出（stdout 仍输出 []）：与 find/get 的 NotFound 契约统一。
                Environment.ExitCode = ExitCodes.NotFound;
            }
        });

        app.Add("list", (string? type = null, string? mod = null, int limit = 20, int offset = 0, bool total = false) =>
        {
            if (TypeGuard.RejectUnknown(type, repository))
                return;

            var results = repository.List(type, mod, limit, offset);
            if (total)
                output.Write(new { total = repository.CountListed(type, mod), results });
            else
                output.Write(results);
        });
    }

    /// <summary>
    /// 0 命中时的方向指引：单裸词已做过子串补充仍 0 命中，即名字与文本中确实不存在该词，
    /// 提示拼写检查与浏览路径；复合 FTS 查询未做子串补充，仍提示前缀通配机制。
    /// </summary>
    private static void MaybePrefixWildcardHint(string keyword)
    {
        if (keyword.Contains('*'))
        {
            Console.Error.WriteLine(
                $"Hint: 0 hits for '{keyword}' even with a prefix wildcard — the keyword may not be a def name; " +
                $"browse with 'list --type <T>' (run 'types' to list valid types)");
            return;
        }

        if (SearchSubstring.IsBareWord(keyword))
        {
            Console.Error.WriteLine(
                $"Hint: no defs match '{keyword}' in names or text — check spelling, or browse with 'list --type <T>' " +
                $"(run 'types' to list valid types)");
            return;
        }

        if (!keyword.Any(char.IsAsciiLetter))
            return;
        Console.Error.WriteLine(
            $"Hint: 0 hits for '{keyword}'. The FTS index tokenizes whole words — " +
            $"compound names like 'ShieldBelt' need a prefix wildcard: 'shield*'");
    }
}
