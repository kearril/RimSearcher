namespace RimSearcher.Core;

public sealed class IndexCacheManifest
{
    public int SchemaVersion { get; init; }
    public string ConfigFingerprint { get; init; } = string.Empty;
    public string IndexFile { get; init; } = string.Empty;
    public string Compression { get; init; } = "gzip";
    public long IndexFileSize { get; init; }
    public string IndexFileSha256 { get; init; } = string.Empty;
    public DateTime BuiltAtUtc { get; init; }
    public long BuildDurationMs { get; init; }
    public int IndexedCsharpFileCount { get; init; }
    public int IndexedXmlFileCount { get; init; }
}

public sealed class IndexCacheSnapshot
{
    public SourceIndexerSnapshot Source { get; init; } = new();
    public DefIndexerSnapshot Def { get; init; } = new();
}

public sealed class SourceIndexerSnapshot
{
    public Dictionary<string, string[]> FileIndex { get; init; } = new();
    public Dictionary<string, string[]> TypeMap { get; init; } = new();
    public Dictionary<string, string> InheritanceMap { get; init; } = new();
    public Dictionary<string, string[]> InheritorsMap { get; init; } = new();
    public Dictionary<string, string[]> ShortTypeMap { get; init; } = new();
    public Dictionary<string, SourceMemberSnapshot[]> MemberIndex { get; init; } = new();
    public Dictionary<string, string[]> NgramIndex { get; init; } = new();
    public string[] ProcessedFiles { get; init; } = Array.Empty<string>();
}

public sealed class SourceMemberSnapshot
{
    public string TypeName { get; init; } = string.Empty;
    public string MemberName { get; init; } = string.Empty;
    public string MemberType { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
}

public sealed class DefIndexerSnapshot
{
    public Dictionary<string, DefLocation> DefNameIndex { get; init; } = new();
    public Dictionary<string, DefLocation> ParentNameIndex { get; init; } = new();
    public Dictionary<string, DefLocation[]> LabelIndex { get; init; } = new();
    public Dictionary<string, DefFieldContentSnapshot[]> FieldContentIndex { get; init; } = new();
    public string[] ProcessedFiles { get; init; } = Array.Empty<string>();
}

public sealed class DefFieldContentSnapshot
{
    public DefLocation Location { get; init; } = new(string.Empty, string.Empty, string.Empty, null, null);
    public string FieldPath { get; init; } = string.Empty;
}
