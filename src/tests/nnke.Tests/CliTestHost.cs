using System.CommandLine;
using Ananke.Tool.Shared;

namespace Ananke.Tool.Tests;

/// <summary>
/// Drives a single <c>nnke</c> command through the same
/// <c>Command.Parse(args).InvokeAsync()</c> pipeline <c>Program.cs</c> uses, wrapped in a
/// minimal root carrying the shared recursive <c>--json</c> option. This exercises the
/// actual exit code System.CommandLine would return to the shell — the same thing B1's
/// findings verified by running the built binary — rather than calling a command's private
/// <c>Execute</c> method directly and inferring the wiring is correct.
/// </summary>
internal static class CliTestHost
{
    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        Command command, params string[] args)
    {
        var root = new RootCommand("test") { CliOptions.CreateJsonOption(), command };

        var originalOut = Console.Out;
        var originalErr = Console.Error;
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();
        Console.SetOut(outWriter);
        Console.SetError(errWriter);

        try
        {
            var exitCode = await root.Parse(args).InvokeAsync();
            return (exitCode, outWriter.ToString(), errWriter.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }
}
