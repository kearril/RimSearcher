using System.Text.Json;
using System.Collections.Concurrent;
using RimSearcher.Server.Tools;

namespace RimSearcher.Server;

/// <summary>
/// Core class for the RimSearcher MCP server, handling JSON-RPC communication and tool dispatching.
/// </summary>
public sealed class RimSearcher
{
    private readonly Dictionary<string, ITool> _tools = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeRequests = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly TextWriter _protocolOut;

    // Limit max concurrency to balance response speed and system resource consumption.
    private readonly SemaphoreSlim _concurrencyLimit = new(10, 10);

    public RimSearcher(TextWriter? protocolOut = null)
    {
        _protocolOut = protocolOut ?? Console.Out;
        // Bind global logger delegate
        ServerLogger.OnLogAsync = (msg, level) => this.LogAsync(msg, level);
    }

    /// <summary>
    /// Registers an MCP tool.
    /// </summary>
    public void RegisterTool(ITool tool)
    {
        _tools[tool.Name] = tool;
    }

    /// <summary>
    /// Starts the server main loop, reading JSON-RPC messages from standard input.
    /// </summary>
    public async Task RunAsync()
    {
        while (true)
        {
            var line = await Console.In.ReadLineAsync();
            if (line == null) break;

            await _concurrencyLimit.WaitAsync();

            _ = Task.Run(async () =>
            {
                object? requestId = null;
                string? requestKey = null;
                CancellationTokenSource? cts = null;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (!root.TryGetProperty("method", out var methodProp))
                    {
                        if (root.TryGetProperty("id", out var errId))
                            await SendResponseAsync(errId, error: new { code = -32600, message = "Invalid Request" });
                        return;
                    }

                    var method = methodProp.GetString();

                    // Handle JSON-RPC cancellation notifications
                    if (method == "$.cancelRequest")
                    {
                        if (root.TryGetProperty("params", out var p) && p.TryGetProperty("id", out var cancelId))
                        {
                            var idToCancel = cancelId.ToString(); // Use string for internal dictionary key
                            if (_activeRequests.TryRemove(idToCancel, out var targetCts))
                            {
                                targetCts.Cancel();
                                await ServerLogger.Debug($"Client cancelled request {idToCancel}");
                            }
                        }
                        return;
                    }

                    bool hasId = root.TryGetProperty("id", out var idProp);
                    if (hasId)
                    {
                        requestId = idProp; // Keep original JsonElement for response to ensure ID type consistency
                        requestKey = idProp.ToString(); // Use string as key for tracking
                        cts = new CancellationTokenSource();
                        _activeRequests[requestKey] = cts;
                    }

                    await HandleRequestAsync(method, requestId, root, cts?.Token ?? CancellationToken.None);
                }
                catch (JsonException)
                {
                    await SendResponseAsync(null, error: new { code = -32700, message = "Parse error" });
                }
                catch (OperationCanceledException)
                {
                    if (requestId != null)
                        await SendResponseAsync(requestId, error: new { code = -32000, message = "Request cancelled" });
                }
                catch (Exception ex)
                {
                    if (requestId != null)
                        await SendResponseAsync(requestId, error: new { code = -32603, message = $"Internal error: {ex.Message}" });
                }
                finally
                {
                    if (requestKey != null) _activeRequests.TryRemove(requestKey, out _);
                    cts?.Dispose();
                    _concurrencyLimit.Release();
                }
            });
        }
    }

    private async Task HandleRequestAsync(string? method, object? id, JsonElement root, CancellationToken ct)
    {
        try
        {
            if (method == "initialize")
            {
                await SendResponseAsync(id, new
                {
                    protocolVersion = "2025-11-25",
                    capabilities = new
                    {
                        tools = new { },
                        logging = new { },
                        progress = new { }
                    },
                    serverInfo = new
                    {
                        name = "RimSearcher-Server",
                        version = "2.2",
                        description = "Specialized MCP server for deep RimWorld source code and XML Def analysis."
                    }
                });
            }
            else if (method == "notifications/initialized")
            {
                await LogAsync("RimSearcher server initialized and ready to handle requests.", "info");
            }
            else if (method == "list_tools" || method == "tools/list")
            {
                if (id == null) return;
                await SendResponseAsync(id, new
                {
                    tools = _tools.Values.Select(t => new
                    {
                        name = t.Name,
                        description = t.Description,
                        inputSchema = t.JsonSchema,
                        annotations = t.Icon != null ? new { icon = t.Icon } : null
                    })
                });
            }
            else if (method == "call_tool" || method == "tools/call")
            {
                if (id == null) return;
                var paramsElem = root.GetProperty("params");
                var toolName = paramsElem.GetProperty("name").GetString();

                if (toolName != null && _tools.TryGetValue(toolName, out var tool))
                {
                    // Create progress reporter to send MCP notifications
                    var progressReporter = new Progress<double>(async p => 
                    {
                        await SendNotificationAsync("notifications/progress", new 
                        {
                            progress = p,
                            total = 1.0,
                            progressToken = id // Progress is tied to the request ID
                        });
                    });

                    var result = await tool.ExecuteAsync(paramsElem.GetProperty("arguments"), ct, progressReporter);
                    await SendResponseAsync(id, new
                    {
                        content = new[] { new { type = "text", text = result.Content } },
                        isError = result.IsError
                    });
                }
                else
                {
                    await SendResponseAsync(id, error: new { code = -32601, message = "Tool not found" });
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            if (id != null)
                await SendResponseAsync(id, error: new { code = -32603, message = $"Internal error: {ex.Message}" });
        }
    }

    /// <summary>
    /// Sends a logging notification to the MCP client.
    /// </summary>
    public async Task LogAsync(string message, string level = "info", string? logger = "RimSearcher")
    {
        await SendNotificationAsync("notifications/logging/message", new
        {
            level = level,
            logger = logger,
            data = message
        });
    }

    private async Task SendNotificationAsync(string method, object? @params = null)
    {
        var notification = new { jsonrpc = "2.0", method = method, @params = @params };
        var json = JsonSerializer.Serialize(notification);

        await _writeLock.WaitAsync();
        try
        {
            await _protocolOut.WriteLineAsync(json);
            await _protocolOut.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task SendResponseAsync(object? id, object? result = null, object? error = null)
    {
        if (id == null && error == null) return;

        object response = error != null
            ? new { jsonrpc = "2.0", id = id, error = error }
            : new { jsonrpc = "2.0", id = id, result = result };

        var json = JsonSerializer.Serialize(response);

        await _writeLock.WaitAsync();
        try
        {
            // Async write and flush to ensure JSON-RPC messages are delivered immediately.
            await _protocolOut.WriteLineAsync(json);
            await _protocolOut.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }
}