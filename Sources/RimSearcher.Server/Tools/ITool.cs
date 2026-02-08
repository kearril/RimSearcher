using System.Text.Json;

namespace RimSearcher.Server.Tools;

/// <summary>
/// Defines a common interface for MCP server tools.
/// </summary>
public interface ITool
{
    /// <summary>
    /// The name of the tool.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Functional description of the tool.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// JSON Schema definition for the tool's input parameters.
    /// </summary>
    object JsonSchema { get; }

    /// <summary>
    /// Optional: Lucide icon name for the tool UI (e.g., "lucide:search").
    /// </summary>
    string? Icon => null;

    /// <summary>
    /// Executes the core logic of the tool.
    /// </summary>
    /// <param name="arguments">JSON parameters passed by the MCP client.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <param name="progress">Optional: Callback to report progress (0.0 to 1.0).</param>
    /// <returns>A ToolResult containing the output and error status.</returns>
    Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken, IProgress<double>? progress = null);
}