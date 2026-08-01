using RimSearcher.Cli.Queries;

namespace RimSearcher.Cli.Infrastructure;

/// <summary>
/// --type 白名单校验：拼错的 def_type 会静默返回空结果，无法区分"类型拼错"与"确实无匹配"，
/// 故在查询前拒绝未知类型并指引 types 命令（exit 1 参数错误语义）。
/// </summary>
internal static class TypeGuard
{
    public static bool RejectUnknown(string? type, DefRepository repository)
    {
        if (type == null || repository.IsKnownType(type))
            return false;

        Console.Error.WriteLine($"Error: unknown def_type '{type}'; run 'rimsearcher types' to list valid types");
        Environment.ExitCode = ExitCodes.Error;
        return true;
    }
}
