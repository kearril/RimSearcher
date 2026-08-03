using System.Net;
using System.Reflection;
using RimSearcher.Cli.Infrastructure;

namespace RimSearcher.Cli.Maintenance;

internal static class UpdateChecker
{
    private const string ApplicationName = "RimSearcher";
    private const string LatestReleaseUrl = "https://github.com/kearril/RimSearcher/releases/latest";
    private const string ReleasePageUrl = "https://github.com/kearril/RimSearcher/releases/tag";

    public static void Check()
    {
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
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

        Console.WriteLine($"Update available: {currentVersion} -> {latestVersion}");
        Console.WriteLine($"Download: {ReleasePageUrl}/{tag}");
        Console.WriteLine("Update both rimsearcher.exe and RimSearcher_DataMod.zip, then re-export defs.db.");
    }
}
