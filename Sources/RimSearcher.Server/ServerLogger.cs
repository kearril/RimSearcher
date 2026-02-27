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

    public static async Task Info(string component, string message, params (string Key, object? Value)[] fields)
        => await LogAsync(Format(component, message, fields), "info");

    public static async Task Warning(string component, string message, params (string Key, object? Value)[] fields)
        => await LogAsync(Format(component, message, fields), "warning");

    public static async Task Error(string component, string message, params (string Key, object? Value)[] fields)
        => await LogAsync(Format(component, message, fields), "error");

    public static async Task Debug(string component, string message, params (string Key, object? Value)[] fields)
        => await LogAsync(Format(component, message, fields), "debug");

    private static string Format(string component, string message, (string Key, object? Value)[] fields)
    {
        var normalizedMessage = Sanitize(message);
        if (fields == null || fields.Length == 0)
        {
            return $"{component}: {normalizedMessage}";
        }

        var parts = fields
            .Where(field => !string.IsNullOrWhiteSpace(field.Key))
            .Select(field => $"{field.Key}={Sanitize(field.Value?.ToString() ?? "null")}")
            .ToArray();

        if (parts.Length == 0)
        {
            return $"{component}: {normalizedMessage}";
        }

        return $"{component}: {normalizedMessage} | {string.Join(", ", parts)}";
    }

    private static string Sanitize(string value)
    {
        return value.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}
