namespace RimSearcher.Server;

public enum McpTransportKind
{
    Stdio,
    StreamableHttp
}

public sealed record ServerCliOptions(
    McpTransportKind Transport,
    string Host,
    int Port,
    string MountPath)
{
    public static ServerCliOptions Parse(string[] args)
    {
        var transport = McpTransportKind.Stdio;
        var host = "127.0.0.1";
        var port = 51234;
        var mountPath = "/mcp";

        for (var i = 0; i < args.Length; i++)
        {
            var (name, inlineValue) = SplitOption(args[i]);
            var value = inlineValue;

            switch (name)
            {
                case "--transport":
                    value ??= ReadValue(args, ref i, name);
                    transport = ParseTransport(value);
                    break;
                case "--host":
                    value ??= ReadValue(args, ref i, name);
                    host = value;
                    break;
                case "--port":
                    value ??= ReadValue(args, ref i, name);
                    if (!int.TryParse(value, out port) || port < 1 || port > 65535)
                    {
                        throw new ArgumentException($"Invalid port '{value}'. Port must be between 1 and 65535.");
                    }
                    break;
                case "--mount-path":
                    value ??= ReadValue(args, ref i, name);
                    mountPath = NormalizeMountPath(value);
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{name}'.");
            }
        }

        return new ServerCliOptions(transport, host, port, mountPath);
    }

    private static (string Name, string? Value) SplitOption(string arg)
    {
        var equalsIndex = arg.IndexOf('=', StringComparison.Ordinal);
        if (equalsIndex < 0)
        {
            return (arg, null);
        }

        return (arg[..equalsIndex], arg[(equalsIndex + 1)..]);
    }

    private static string ReadValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for {optionName}.");
        }

        index++;
        return args[index];
    }

    private static McpTransportKind ParseTransport(string value)
    {
        return value switch
        {
            "stdio" => McpTransportKind.Stdio,
            "streamable-http" => McpTransportKind.StreamableHttp,
            _ => throw new ArgumentException($"Unsupported transport '{value}'. Use 'stdio' or 'streamable-http'.")
        };
    }

    private static string NormalizeMountPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Mount path cannot be empty.");
        }

        var trimmed = value.Trim();
        var hasSlash = trimmed.StartsWith("/", StringComparison.Ordinal);
        return hasSlash ? trimmed : "/" + trimmed;
    }
}
