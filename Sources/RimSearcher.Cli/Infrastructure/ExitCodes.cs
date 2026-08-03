namespace RimSearcher.Cli.Infrastructure;

/// <summary>
/// 进程退出码约定：0 成功、1 运行时错误、2 查询无结果或存在歧义。
/// 退出码与 stderr 消息文本属于 CLI 外部契约，变更需同步 skills/rimsearcher/SKILL.md。
/// </summary>
internal static class ExitCodes
{
    public const int Success = 0;
    public const int Error = 1;
    public const int NotFound = 2;
}
