using System.Text.Json;

namespace RimSearcher.Server.Tools;

public interface ITool
{
    string Name { get; }
    string Description { get; }
    object JsonSchema { get; }
    string? Icon => null;

    Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken, IProgress<double>? progress = null);
}