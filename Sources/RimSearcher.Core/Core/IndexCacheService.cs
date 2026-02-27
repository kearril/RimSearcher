using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RimSearcher.Core;

public static class IndexCacheService
{
    public const int SchemaVersion = 1;//缓存结构版本号

    private const string ManifestFileName = "manifest.json";
    private const string IndexFileName = "index.bin";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string GetDefaultCacheDirectory()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".cache", "index");
    }

    public static bool EnsureCacheDirectory(string cacheDirectory, out string? error)
    {
        error = null;
        try
        {
            Directory.CreateDirectory(cacheDirectory);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static string ComputeConfigFingerprint(IEnumerable<string> csharpPaths, IEnumerable<string> xmlPaths)
    {
        var normalizedCsharp = NormalizePaths(csharpPaths);
        var normalizedXml = NormalizePaths(xmlPaths);

        var builder = new StringBuilder();
        builder.AppendLine($"schema:{SchemaVersion}");
        builder.AppendLine("[csharp]");
        foreach (var path in normalizedCsharp)
        {
            builder.AppendLine(path);
        }

        builder.AppendLine("[xml]");
        foreach (var path in normalizedXml)
        {
            builder.AppendLine(path);
        }

        return $"sha256:{ComputeSha256(Encoding.UTF8.GetBytes(builder.ToString()))}";
    }

    public static (bool Success, string Reason, IndexCacheSnapshot? Snapshot, IndexCacheManifest? Manifest) TryLoad(
        string cacheDirectory,
        string expectedConfigFingerprint)
    {
        try
        {
            var manifestPath = Path.Combine(cacheDirectory, ManifestFileName);
            if (!File.Exists(manifestPath))
                return (false, "manifest missing", null, null);

            var manifestJson = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<IndexCacheManifest>(manifestJson, ManifestJsonOptions);
            if (manifest == null)
                return (false, "manifest parse failed", null, null);

            if (manifest.SchemaVersion != SchemaVersion)
                return (false, $"schema mismatch (expected {SchemaVersion}, got {manifest.SchemaVersion})", null, manifest);

            if (!string.Equals(manifest.ConfigFingerprint, expectedConfigFingerprint, StringComparison.Ordinal))
                return (false, "config fingerprint mismatch", null, manifest);

            var indexFile = string.IsNullOrWhiteSpace(manifest.IndexFile) ? IndexFileName : manifest.IndexFile;
            var indexPath = Path.Combine(cacheDirectory, indexFile);
            if (!File.Exists(indexPath))
                return (false, "index file missing", null, manifest);

            var compressedBytes = File.ReadAllBytes(indexPath);
            if (manifest.IndexFileSize > 0 && compressedBytes.LongLength != manifest.IndexFileSize)
                return (false, "index file size mismatch", null, manifest);

            if (!string.IsNullOrWhiteSpace(manifest.IndexFileSha256))
            {
                var actualHash = ComputeSha256(compressedBytes);
                if (!string.Equals(actualHash, manifest.IndexFileSha256, StringComparison.OrdinalIgnoreCase))
                    return (false, "index file hash mismatch", null, manifest);
            }

            var snapshotBytes = string.Equals(manifest.Compression, "gzip", StringComparison.OrdinalIgnoreCase)
                ? Decompress(compressedBytes)
                : compressedBytes;

            var snapshot = JsonSerializer.Deserialize<IndexCacheSnapshot>(snapshotBytes, SnapshotJsonOptions);
            if (snapshot == null)
                return (false, "snapshot parse failed", null, manifest);

            return (true, "cache loaded", snapshot, manifest);
        }
        catch (Exception ex)
        {
            return (false, $"cache load exception: {ex.Message}", null, null);
        }
    }

    public static (bool Success, string Reason, IndexCacheManifest? Manifest) Save(
        string cacheDirectory,
        string configFingerprint,
        IndexCacheSnapshot snapshot,
        TimeSpan buildDuration,
        int indexedCsharpFileCount,
        int indexedXmlFileCount)
    {
        try
        {
            Directory.CreateDirectory(cacheDirectory);

            var snapshotJsonBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, SnapshotJsonOptions);
            var compressedBytes = Compress(snapshotJsonBytes);
            var compressedHash = ComputeSha256(compressedBytes);

            var manifest = new IndexCacheManifest
            {
                SchemaVersion = SchemaVersion,
                ConfigFingerprint = configFingerprint,
                IndexFile = IndexFileName,
                Compression = "gzip",
                IndexFileSize = compressedBytes.LongLength,
                IndexFileSha256 = compressedHash,
                BuiltAtUtc = DateTime.UtcNow,
                BuildDurationMs = (long)buildDuration.TotalMilliseconds,
                IndexedCsharpFileCount = indexedCsharpFileCount,
                IndexedXmlFileCount = indexedXmlFileCount
            };

            var indexPath = Path.Combine(cacheDirectory, IndexFileName);
            var manifestPath = Path.Combine(cacheDirectory, ManifestFileName);

            WriteBytesAtomic(indexPath, compressedBytes);
            var manifestJson = JsonSerializer.Serialize(manifest, ManifestJsonOptions);
            WriteTextAtomic(manifestPath, manifestJson);

            return (true, "cache saved", manifest);
        }
        catch (Exception ex)
        {
            return (false, $"cache save exception: {ex.Message}", null);
        }
    }

    private static List<string> NormalizePaths(IEnumerable<string> paths)
    {
        var comparer = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        var set = new HashSet<string>(comparer);
        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            try
            {
                var full = Path.GetFullPath(raw).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    full = full.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                }
                set.Add(full);
            }
            catch
            {
            }
        }

        return set.OrderBy(x => x, comparer).ToList();
    }

    private static byte[] Compress(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(bytes, 0, bytes.Length);
        }
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static string ComputeSha256(byte[] bytes)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

    private static void WriteBytesAtomic(string targetPath, byte[] bytes)
    {
        var tempPath = targetPath + ".tmp";
        File.WriteAllBytes(tempPath, bytes);
        ReplaceFile(tempPath, targetPath);
    }

    private static void WriteTextAtomic(string targetPath, string content)
    {
        var tempPath = targetPath + ".tmp";
        File.WriteAllText(tempPath, content);
        ReplaceFile(tempPath, targetPath);
    }

    private static void ReplaceFile(string tempPath, string targetPath)
    {
        if (!File.Exists(targetPath))
        {
            File.Move(tempPath, targetPath);
            return;
        }

        try
        {
            File.Replace(tempPath, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        catch
        {
            File.Delete(targetPath);
            File.Move(tempPath, targetPath);
        }
    }
}
