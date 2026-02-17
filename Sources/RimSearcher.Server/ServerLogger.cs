namespace RimSearcher.Server;

public static class ServerLogger
{
    public static Func<string, string, Task>? OnLogAsync;

    public static async Task LogAsync(string message, string level = "info")
    {
        if (OnLogAsync != null)
        {
            await OnLogAsync(message, level);
        }
        else
        {
            await Console.Error.WriteLineAsync($"[{level.ToUpper()}] {message}");
        }
    }

    public static async Task Info(string message) => await LogAsync(message, "info");
    public static async Task Error(string message) => await LogAsync(message, "error");
    public static async Task Warning(string message) => await LogAsync(message, "warning");
    public static async Task Debug(string message) => await LogAsync(message, "debug");
}
