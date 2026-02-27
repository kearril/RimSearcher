using System.Net.Http;
using System.Text.Json;
using RimSearcher.Core;

namespace RimSearcher.Server;

public static class UpdateChecker
{
    public const string CurrentVersion = "2.6";//版本号
    private const string GitHubApiUrl = "https://api.github.com/repos/kearril/RimSearcher/releases/latest";
    private static string CacheFilePath
    {
        get
        {
            var indexCacheDir = IndexCacheService.GetDefaultCacheDirectory();
            var parent = Path.GetDirectoryName(indexCacheDir) ?? indexCacheDir;
            return Path.Combine(parent, ".update-cache");
        }
    }
    
    public static async Task CheckAsync()
    {
        try
        {
            if (TryReadCache(out var cachedVersion, out var cachedTime))
            {
                if (DateTime.UtcNow - cachedTime < TimeSpan.FromHours(24))
                {
                    if (cachedVersion != null && IsNewer(cachedVersion, CurrentVersion))
                    {
                        await NotifyUpdate(cachedVersion);
                    }
                    return;
                }
            }

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            httpClient.DefaultRequestHeaders.Add("User-Agent", $"RimSearcher/{CurrentVersion}");
            httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

            var response = await httpClient.GetStringAsync(GitHubApiUrl);
            using var doc = JsonDocument.Parse(response);

            if (doc.RootElement.TryGetProperty("tag_name", out var tagProp))
            {
                var latestVersion = tagProp.GetString()?.TrimStart('v', 'V');
                if (!string.IsNullOrEmpty(latestVersion))
                {
                    WriteCache(latestVersion);

                    if (IsNewer(latestVersion, CurrentVersion))
                    {
                        await NotifyUpdate(latestVersion);
                    }
                }
            }
        }
        catch
        {
        }
    }

    private static async Task NotifyUpdate(string latestVersion)
    {
        await ServerLogger.Warning("UpdateChecker", "New version is available",
            ("current", CurrentVersion),
            ("latest", latestVersion),
            ("url", "https://github.com/kearril/RimSearcher/releases/latest"));
    }

    private static bool IsNewer(string remote, string local)
    {
        try
        {
            var remoteParts = remote.Split('.').Select(int.Parse).ToArray();
            var localParts = local.Split('.').Select(int.Parse).ToArray();
            var maxLen = Math.Max(remoteParts.Length, localParts.Length);

            for (int i = 0; i < maxLen; i++)
            {
                var r = i < remoteParts.Length ? remoteParts[i] : 0;
                var l = i < localParts.Length ? localParts[i] : 0;
                if (r > l) return true;
                if (r < l) return false;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadCache(out string? version, out DateTime checkTime)
    {
        version = null;
        checkTime = DateTime.MinValue;

        try
        {
            if (!File.Exists(CacheFilePath)) return false;
            var lines = File.ReadAllLines(CacheFilePath);
            if (lines.Length < 2) return false;

            version = lines[0].Trim();
            checkTime = DateTime.Parse(lines[1].Trim(), null, System.Globalization.DateTimeStyles.RoundtripKind);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteCache(string version)
    {
        try
        {
            var dir = Path.GetDirectoryName(CacheFilePath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            File.WriteAllLines(CacheFilePath, new[]
            {
                version,
                DateTime.UtcNow.ToString("O")
            });
        }
        catch
        {
        }
    }
}
