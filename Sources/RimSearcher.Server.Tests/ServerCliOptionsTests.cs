using RimSearcher.Server;
using Xunit;

namespace RimSearcher.Server.Tests;

public sealed class ServerCliOptionsTests
{
    [Fact]
    public void Parse_DefaultsToStdioAndLocalHttpOptions()
    {
        var options = ServerCliOptions.Parse([]);

        Assert.Equal(McpTransportKind.Stdio, options.Transport);
        Assert.Equal("127.0.0.1", options.Host);
        Assert.Equal(51234, options.Port);
        Assert.Equal("/mcp", options.MountPath);
    }

    [Fact]
    public void Parse_ReadsExplicitStreamableHttpOptions()
    {
        var options = ServerCliOptions.Parse([
            "--transport", "streamable-http",
            "--host", "localhost",
            "--port", "3000",
            "--mount-path", "mcp"
        ]);

        Assert.Equal(McpTransportKind.StreamableHttp, options.Transport);
        Assert.Equal("localhost", options.Host);
        Assert.Equal(3000, options.Port);
        Assert.Equal("/mcp", options.MountPath);
    }

    [Fact]
    public void Parse_AllowsEqualsSyntax()
    {
        var options = ServerCliOptions.Parse([
            "--transport=streamable-http",
            "--host=127.0.0.1",
            "--port=51235",
            "--mount-path=/custom-mcp"
        ]);

        Assert.Equal(McpTransportKind.StreamableHttp, options.Transport);
        Assert.Equal("127.0.0.1", options.Host);
        Assert.Equal(51235, options.Port);
        Assert.Equal("/custom-mcp", options.MountPath);
    }

    [Fact]
    public void Parse_RejectsUnsupportedTransport()
    {
        var ex = Assert.Throws<ArgumentException>(() => ServerCliOptions.Parse(["--transport", "sse"]));

        Assert.Contains("Unsupported transport", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_RejectsInvalidPort()
    {
        var ex = Assert.Throws<ArgumentException>(() => ServerCliOptions.Parse(["--port", "abc"]));

        Assert.Contains("Invalid port", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
