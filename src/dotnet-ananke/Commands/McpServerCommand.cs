using Ananke.Tool.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using System.CommandLine;

namespace Ananke.Tool.Commands;

/// <summary>
/// Handles <c>nnke mcp-server</c> — launches <c>nnke</c> as a stdio MCP server,
/// exposing all commands as MCP tools that any MCP client (Claude Desktop,
/// VS Code Copilot, Cursor, etc.) can call.
/// </summary>
/// <remarks>
/// <para>
/// The MCP server wraps the same logic as the CLI commands:
/// <c>ananke_docs_search</c>, <c>ananke_docs_read</c>, <c>ananke_inspect</c>,
/// <c>ananke_explain</c>, <c>ananke_patterns_list</c>, <c>ananke_validate</c>, etc.
/// </para>
/// <para>
/// All communication is over stdin/stdout (JSON-RPC). No network ports, no HTTP, no cloud.
/// The server is a local process launched by the MCP client.
/// </para>
/// </remarks>
internal static class McpServerCommand
{
    public static Command Create()
    {
        var command = new Command("mcp-server",
            "Launch nnke as an MCP server (stdio). Exposes all commands as tools for Claude, Copilot, Cursor, etc.");

        command.SetAction(async _ =>
        {
            await RunAsync();
        });

        return command;
    }

    private static async Task RunAsync()
    {
        // CreateEmptyApplicationBuilder prevents default console logging
        // from corrupting the JSON-RPC messages on stdout.
        var builder = Host.CreateEmptyApplicationBuilder(settings: null);

        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new()
                {
                    Name = "ananke",
                    Version = GetVersion()
                };
            })
            .WithStdioServerTransport()
            .WithTools(McpToolRegistry.CreateAll());

        await builder.Build().RunAsync();
    }

    private static string GetVersion()
    {
        var assembly = typeof(McpServerCommand).Assembly;
        var version = assembly.GetName().Version;
        return version is not null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
    }
}
