using System;
using System.IO;
using System.Linq;

namespace RimSearcher.Cli.Maintenance;

internal static class PathInstaller
{
    public static void Install()
    {
        var executableDirectory = Path.GetDirectoryName(Environment.ProcessPath)!;
        var currentPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User) ?? string.Empty;

        if (currentPath.Split(';').Any(path => path.Equals(executableDirectory, StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("rimsearcher is already in PATH.");
            return;
        }

        Environment.SetEnvironmentVariable(
            "Path",
            currentPath.TrimEnd(';') + ";" + executableDirectory,
            EnvironmentVariableTarget.User);

        Console.WriteLine($"rimsearcher added to user PATH.\nPath: {executableDirectory}\nRestart your terminal to use it globally.");
    }
}
