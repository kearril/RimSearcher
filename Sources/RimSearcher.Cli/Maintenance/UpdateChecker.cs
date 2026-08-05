using System;
using System.Net;
using System.Net.Http;
using System.Reflection;

namespace RimSearcher.Cli.Maintenance;

internal static class UpdateChecker
{
    private const string ApplicationName = "RimSearcher";
    private const string LatestReleaseUrl = "https://github.com/kearril/RimSearcher/releases/latest";
    private const string ReleasePageUrl = "https://github.com/kearril/RimSearcher/releases/tag";

    public static void Check()
    {
        // 失败路径的等待上限固定为10s
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(ApplicationName);

        string tag = null!;
        try
        {
            using var response = http.GetAsync(LatestReleaseUrl).Result;
            if (response.StatusCode != HttpStatusCode.Redirect)
                throw new Exception($"Unexpected status: {(int)response.StatusCode}");

            var location = response.Headers.Location?.ToString()
                ?? throw new Exception("No Location header in redirect");
            tag = location[(location.LastIndexOf('/') + 1)..];
        }
        catch (Exception exception)
        {
            // 失败不打断分析。stderr 保留可见性，自然 return（exit 0）保脚本语义。
            Console.Error.WriteLine($"Failed to check for updates: {exception.Message}");
            return;
        }

        var latestVersion = tag.StartsWith('v') ? tag[1..] : tag;
        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version!;
        var currentVersion = $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";

        // tag 来自任意 GitHub Release：预发布、空或短段 tag 会让 new Version 抛 FormatException 崩溃，
        // 解析失败与 catch 一样走忽略路径（契约 Ignore check failures），只提示不打断。
        if (!Version.TryParse(latestVersion, out var latestParsed)
            || !Version.TryParse(currentVersion, out var currentParsed))
        {
            Console.Error.WriteLine($"Failed to check for updates: invalid version tag '{tag}'");
            return;
        }

        if (latestParsed <= currentParsed)
        {
            Console.WriteLine($"rimsearcher is up to date ({currentVersion})");
            return;
        }

        Console.WriteLine($"Update available: {currentVersion} -> {latestVersion}");
        Console.WriteLine($"Download: {ReleasePageUrl}/{tag}");
        Console.WriteLine("Update both rimsearcher.exe and RimSearcher_DataMod.zip, then re-export defs.db.");
    }
}
