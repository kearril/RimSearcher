using System.Text.Json;
using RimSearcher.Server;
using RimSearcher.Server.Tools;
using Xunit;

namespace RimSearcher.Server.Tests;

public sealed class RimSearcherRuntimeTests
{
    [Fact]
    public async Task HandleMessageAsync_ReturnsInitializeResponse()
    {
        var server = new RimSearcher(TextWriter.Null, emitLogNotifications: false);

        var messages = await server.HandleMessageAsync("""
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}
            """);

        var response = Assert.Single(messages);
        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;

        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal(1, root.GetProperty("id").GetInt32());
        Assert.Equal("2025-11-25", root.GetProperty("result").GetProperty("protocolVersion").GetString());
        Assert.Equal("RimSearcher-Server", root.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString());
    }

    [Fact]
    public async Task HandleMessageAsync_ReturnsRegisteredTools()
    {
        var server = new RimSearcher(TextWriter.Null, emitLogNotifications: false);
        server.RegisterTool(new FakeTool());

        var messages = await server.HandleMessageAsync("""
            {"jsonrpc":"2.0","id":"tools","method":"tools/list","params":{}}
            """);

        var response = Assert.Single(messages);
        using var document = JsonDocument.Parse(response);
        var tools = document.RootElement.GetProperty("result").GetProperty("tools");

        Assert.Single(tools.EnumerateArray());
        Assert.Equal("fake_tool", tools[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task HandleMessageAsync_ReturnsParseErrorForInvalidJson()
    {
        var server = new RimSearcher(TextWriter.Null, emitLogNotifications: false);

        var messages = await server.HandleMessageAsync("{");

        var response = Assert.Single(messages);
        using var document = JsonDocument.Parse(response);

        Assert.True(document.RootElement.TryGetProperty("error", out var error));
        Assert.Equal(-32700, error.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task HandleMessageAsync_IgnoresJsonRpcResponseBodies()
    {
        var server = new RimSearcher(TextWriter.Null, emitLogNotifications: false);

        var messages = await server.HandleMessageAsync("""
            {"jsonrpc":"2.0","id":1,"result":{}}
            """);

        Assert.Empty(messages);
    }

    private sealed class FakeTool : ITool
    {
        public string Name => "fake_tool";
        public string Description => "Fake tool for runtime tests.";
        public object JsonSchema => new { type = "object", additionalProperties = false };

        public Task<ToolResult> ExecuteAsync(
            JsonElement arguments,
            CancellationToken cancellationToken,
            IProgress<double>? progress = null)
        {
            return Task.FromResult(new ToolResult("fake result"));
        }
    }
}
