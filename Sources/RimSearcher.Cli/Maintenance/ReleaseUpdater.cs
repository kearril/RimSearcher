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
        var executableDirectory = Path.GetDirectoryName(Environment.ProcessPath)!;

        // 上次后台替换失败会留下标记文件：提示而不是再次静默尝试。
        var errorMarkerPath = Path.Combine(executableDirectory, "rimsearcher.update.err");
        if (File.Exists(errorMarkerPath))
        {
            Console.Error.WriteLine($"Previous auto-update failed: {File.ReadAllText(errorMarkerPath).Trim()}");
            TryDelete(errorMarkerPath);
        }

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
        var newExecutablePath = Path.Combine(executableDirectory, "rimsearcher.new.exe");
        TryDelete(newExecutablePath); // 清理上次失败的残留，避免被占用时 File.Create 抛错。

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

        // 更新器副本：复制自身为独立文件再运行。
        // 副本进程锁的是副本文件，目标 rimsearcher.exe 不被任何进程持有，替换才可能成功；
        // 若直接以自身启动 --internal-replace，子进程会锁住目标导致覆盖必败。
        // 不用 bat：UTF-8 脚本被 cmd 按 ANSI 代码页（中文系统 GBK）误读导致 move 静默失败，
        // 且 bat 是 Windows-only，与多平台目标冲突。
        var updaterPath = Path.Combine(executableDirectory, "rimsearcher.updater.exe");
        try
        {
            TryDelete(updaterPath); // 清理上次运行的残留副本。
            File.Copy(Environment.ProcessPath!, updaterPath, overwrite: true);
            Process.Start(new ProcessStartInfo(updaterPath,
                $"\"--internal-replace\" \"{newExecutablePath}\" \"{Environment.ProcessPath}\" {Environment.ProcessId}")
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

        Console.WriteLine($"Downloaded {latestVersion}, installing in background...");
        Console.WriteLine("Run 'rimsearcher --version' in a few seconds to confirm; a failed swap leaves rimsearcher.update.err next to the exe.");
        Environment.Exit(ExitCodes.Success);
    }

    /// <summary>
    /// 内部替换命令（--internal-replace，经 Program.cs 拦截，不进 help）：
    /// 由 updater 副本进程执行，在主进程退出后替换目标 exe。
    /// </summary>
    public static int InternalReplace(string newPath, string targetPath, string parentPid)
    {
        // 等待父进程退出（200ms 轮询，最多 30s）：目标 exe 的锁随父进程退出释放。
        if (int.TryParse(parentPid, out var pid))
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    if (Process.GetProcessById(pid).HasExited)
                        break;
                }
                catch (ArgumentException)
                {
                    break; // 进程已不存在，锁已释放。
                }
                Thread.Sleep(200);
            }
        }

        // 重试应对短暂占用（杀软扫描、并发实例）。
        Exception? lastError = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                File.Move(newPath, targetPath, overwrite: true);
                lastError = null;
                break;
            }
            catch (Exception exception)
            {
                lastError = exception;
                Thread.Sleep(500 * (attempt + 1));
            }
        }

        var directory = Path.GetDirectoryName(targetPath)!;
        if (lastError == null)
        {
            TryDelete(Path.Combine(directory, "rimsearcher.update.err")); // 成功时清除历史失败标记。
            Console.WriteLine("rimsearcher updated successfully.");
        }
        else
        {
            File.WriteAllText(Path.Combine(directory, "rimsearcher.update.err"),
                $"{lastError.GetType().Name}: {lastError.Message}");
            Console.Error.WriteLine($"Update failed: {lastError.Message}");
            TrySelfDelete();
            return ExitCodes.Error;
        }

        TrySelfDelete();
        return ExitCodes.Success;
    }

    /// <summary>
    /// 自删当前进程的 exe：POSIX 允许删除运行中的文件；Windows 需 cmd 兜底（进程退出后执行）。
    /// 兜底失败也无害：下次 update 前会清理残留副本。
    /// </summary>
    private static void TrySelfDelete()
    {
        try
        {
            File.Delete(Environment.ProcessPath!);
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo("cmd", $"/c del \"{Environment.ProcessPath}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            catch
            {
                // 清理失败无害：下次 update 前会再清理残留副本。
            }
        }
    }

    private static void TryDelete(string path)
    {
        // 清理失败不得掩盖原始下载错误，静默忽略即可。
        try { File.Delete(path); } catch { }
    }
}
