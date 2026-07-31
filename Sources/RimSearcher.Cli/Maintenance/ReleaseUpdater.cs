using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using RimSearcher.Cli.Infrastructure;

namespace RimSearcher.Cli.Maintenance;

internal static class ReleaseUpdater
{
    private const string ApplicationName = "RimSearcher";
    private const string LatestReleaseUrl = "https://github.com/kearril/RimSearcher/releases/latest";
    private const string ReleaseDownloadUrl = "https://github.com/kearril/RimSearcher/releases/download";

    public static void Update()
    {
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        http.DefaultRequestHeaders.UserAgent.ParseAdd(ApplicationName);

        string tag = null!;
        try
        {
            var response = http.GetAsync(LatestReleaseUrl).Result;
            if (response.StatusCode != System.Net.HttpStatusCode.Redirect)
                throw new Exception($"Unexpected status: {(int)response.StatusCode}");
            var location = response.Headers.Location?.ToString()
                ?? throw new Exception("No Location header in redirect");
            tag = location[(location.LastIndexOf('/') + 1)..];
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Failed to check for updates: {exception.Message}");
            Environment.Exit(ExitCodes.Error);
        }

        var latestVersion = tag.StartsWith('v') ? tag[1..] : tag;
        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version!;
        var currentVersion = $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";

        if (new Version(latestVersion) <= new Version(currentVersion))
        {
            Console.WriteLine($"rimsearcher is up to date ({currentVersion})");
            return;
        }

        var downloadUrl = $"{ReleaseDownloadUrl}/{tag}/rimsearcher.exe";
        var executableDirectory = Path.GetDirectoryName(Environment.ProcessPath)!;
        var newExecutablePath = Path.Combine(executableDirectory, "rimsearcher.new.exe");

        try
        {
            using var downloader = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
            downloader.DefaultRequestHeaders.UserAgent.ParseAdd(ApplicationName);
            using var stream = downloader.GetStreamAsync(downloadUrl).Result;
            using var file = File.Create(newExecutablePath);
            stream.CopyTo(file);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Download failed: {exception.Message}");
            TryDelete(newExecutablePath);
            Environment.Exit(ExitCodes.Error);
        }

        var batchPath = Path.Combine(executableDirectory, "rimsearcher.update.bat");
        File.WriteAllText(batchPath, $"@echo off\r\ntimeout /t 2 /nobreak > nul\r\nmove /y \"{newExecutablePath}\" \"{Environment.ProcessPath}\"\r\ndel \"%~f0\"\r\n");

        try
        {
            Process.Start(new ProcessStartInfo("cmd", $"/c \"{batchPath}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Failed to start update script: {exception.Message}");
            Console.WriteLine($"New version downloaded to: {newExecutablePath}");
            Environment.Exit(ExitCodes.Error);
        }

        Console.WriteLine($"Downloaded {latestVersion}, installing...");
        Environment.Exit(ExitCodes.Success);
    }

    private static void TryDelete(string path)
    {
        // 清理失败不得掩盖原始下载错误，静默忽略即可。
        try { File.Delete(path); } catch { }
    }
}
