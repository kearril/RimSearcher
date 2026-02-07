using System.Text.Json;
using RimSearcher.Server.Tools;

namespace RimSearcher.Server;

public sealed class RimSearcher
{
    private readonly Dictionary<string, ITool> _tools = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly TextWriter _protocolOut;
    
    // Limit max concurrency to balance response speed and system resource consumption.
    private readonly SemaphoreSlim _concurrencyLimit = new(10, 10);

    public RimSearcher(TextWriter? protocolOut = null)
    {
        _protocolOut = protocolOut ?? Console.Out;
    }

    public void RegisterTool(ITool tool)
    {
        _tools[tool.Name] = tool;
    }

    public async Task RunAsync()
    {
        while (true)
        {
            var line = await Console.In.ReadLineAsync();
            if (line == null) break;

            await _concurrencyLimit.WaitAsync();

            _ = Task.Run(async () =>
            {
                object? id = null;
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
                    bool hasId = root.TryGetProperty("id", out var idProp);
                    if (hasId) id = idProp;

                    await HandleRequestAsync(method, id, root);
                }
                catch (JsonException)
                {
                    await SendResponseAsync(null, error: new { code = -32700, message = "Parse error" });
                }
                catch (Exception ex)
                {
                    if (id != null)
                        await SendResponseAsync(id, error: new { code = -32603, message = $"Internal error: {ex.Message}" });
                }
                finally
                {
                    _concurrencyLimit.Release();
                }
            });
        }
    }

    private async Task HandleRequestAsync(string? method, object? id, JsonElement root)
    {
        try
        {
            if (method == "initialize")
            {
                await SendResponseAsync(id, new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { tools = new { } },
                    serverInfo = new { 
                        name = "RimWorld-Expert-Source-Analyzer", 
                        version = "1.1",
                        description = "Specialized MCP server for deep RimWorld source code and XML Def analysis."
                    }
                });
            }
            else if (method == "notifications/initialized")
            {
                await Console.Error.WriteLineAsync("MCP handshake complete.");
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
                        inputSchema = t.JsonSchema
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
                    var result = await tool.ExecuteAsync(paramsElem.GetProperty("arguments"));
                    await SendResponseAsync(id, new { content = new[] { new { type = "text", text = result } } });
                }
                else
                {
                    await SendResponseAsync(id, error: new { code = -32601, message = "Tool not found" });
                }
            }
        }
        catch (Exception ex)
        {
            if (id != null)
                await SendResponseAsync(id, error: new { code = -32603, message = $"Internal error: {ex.Message}" });
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