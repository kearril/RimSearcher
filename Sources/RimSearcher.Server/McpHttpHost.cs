using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace RimSearcher.Server;

public static class McpHttpHost
{
    public static async Task RunAsync(RimSearcher server, ServerCliOptions options, CancellationToken cancellationToken = default)
    {
        var app = Build(server, options);
        await app.RunAsync(cancellationToken);
    }

    public static WebApplication Build(RimSearcher server, ServerCliOptions options)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://{options.Host}:{options.Port}");

        var app = builder.Build();
        var mountPath = NormalizeMountPath(options.MountPath);

        app.MapGet(mountPath, HandleGetAsync);
        app.MapPost(mountPath, context => HandlePostAsync(context, server));

        return app;
    }

    public static Task HandleGetAsync(HttpContext context)
    {
        if (!IsAllowedOrigin(context.Request.Headers.Origin.ToString()))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        return Task.CompletedTask;
    }

    public static async Task HandlePostAsync(HttpContext context, RimSearcher server)
    {
        if (!IsAllowedOrigin(context.Request.Headers.Origin.ToString()))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(context.RequestAborted);
        var messages = await server.HandleMessageAsync(body, context.RequestAborted);
        var response = FindJsonRpcResponse(messages);

        if (response == null)
        {
            context.Response.StatusCode = StatusCodes.Status202Accepted;
            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(response, Encoding.UTF8, context.RequestAborted);
    }

    private static bool IsAllowedOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return true;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address);
    }

    private static string? FindJsonRpcResponse(IReadOnlyList<string> messages)
    {
        foreach (var message in messages)
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            var hasId = root.TryGetProperty("id", out _);
            var hasResult = root.TryGetProperty("result", out _);
            var hasError = root.TryGetProperty("error", out _);

            if (hasId && (hasResult || hasError))
            {
                return message;
            }
        }

        return null;
    }

    private static string NormalizeMountPath(string mountPath)
    {
        var hasSlash = mountPath.StartsWith("/", StringComparison.Ordinal);
        return hasSlash ? mountPath : "/" + mountPath;
    }
}
