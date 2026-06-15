using Ananke.Abstractions.Providers;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Tools.Gating;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace Ananke.MCP;

/// <summary>
/// Extension methods for <see cref="ToolKit"/> to import tools from an external MCP server.
/// </summary>
/// <example>
/// <code>
/// await using var client = await McpClient.CreateAsync(
///     new StdioClientTransport(new StdioClientTransportOptions { ... }));
///
/// var toolkit = await new ToolKit("remote")
///     .AddMcpServerToolsAsync(client);
/// </code>
/// </example>
public static class ToolKitMcpExtensions
{
    /// <summary>
    /// Discovers all tools exposed by the <paramref name="client"/> MCP server and registers
    /// them as <see cref="ToolDefinition"/> entries in this <see cref="ToolKit"/>.
    /// Each remote tool becomes callable through the normal <c>ToolKit</c> /
    /// <c>AgentJob</c> pipeline — the MCP call is transparent to the agent.
    /// </summary>
    /// <param name="toolkit">The toolkit to populate.</param>
    /// <param name="client">A connected MCP client (e.g. from <c>McpClient.CreateAsync</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The same <paramref name="toolkit"/> for fluent chaining.</returns>
    /// <remarks>
    /// The caller owns the <paramref name="client"/> lifetime. The returned tool definitions
    /// hold a reference to the client and will fail if it is disposed before tool execution.
    /// </remarks>
    public static async Task<ToolKit> AddMcpServerToolsAsync(
        this ToolKit toolkit,
        McpClient client,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toolkit);
        ArgumentNullException.ThrowIfNull(client);

        var mcpTools = await client.ListToolsAsync(cancellationToken: cancellationToken);

        foreach (var mcpTool in mcpTools)
        {
            var tool = CreateToolDefinition(mcpTool.ProtocolTool, client);
            toolkit.AddTool(tool);
        }

        return toolkit;
    }

    private static ToolDefinition CreateToolDefinition(Tool protocolTool, McpClient client)
    {
        var parameters = ParseParameters(protocolTool.InputSchema);
        var toolName = protocolTool.Name;

        return new ToolDefinition
        {
            Name = toolName,
            Description = protocolTool.Description ?? string.Empty,
            Parameters = parameters,
            ExecutionMode = ToolExecutionMode.Mcp,
            Execute = async (args, ct) =>
            {
                var mcpArgs = ToMcpArguments(args);
                var result = await client.CallToolAsync(toolName, mcpArgs, cancellationToken: ct);
                var text = ExtractText(result);
                return result.IsError == true ? ToolResult.Error(text) : ToolResult.Ok(text);
            }
        };
    }

    /// <summary>
    /// Discovers all tools from a pool of MCP servers and registers them as
    /// <see cref="ToolDefinition"/> entries that are load-balanced across the pool
    /// at call time.
    /// </summary>
    /// <param name="toolkit">The toolkit to populate.</param>
    /// <param name="clients">
    /// Two or more connected MCP clients forming the pool. Caller owns their lifetime.
    /// </param>
    /// <param name="faultObserver">
    /// Optional fault observer. When set, server-side call failures are reported so
    /// health state and affinity tracking stay current.
    /// </param>
    /// <param name="faultThreshold">
    /// Consecutive failures before a server is skipped in rotation. Defaults to 3.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The same <paramref name="toolkit"/> for fluent chaining.</returns>
    /// <remarks>
    /// Tool discovery uses the first client in the pool (all replicas are assumed to
    /// expose identical tool schemas). Execution is distributed across the pool.
    /// </remarks>
    public static async Task<ToolKit> AddMcpPoolAsync(
        this ToolKit toolkit,
        IReadOnlyList<McpClient> clients,
        IToolFaultObserver? faultObserver = null,
        int faultThreshold = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toolkit);
        if (clients is null || clients.Count == 0)
            throw new ArgumentException("At least one MCP client is required.", nameof(clients));

        var invoker = new McpToolInvoker(clients, faultObserver, faultThreshold);

        // Discover tool schemas from the first client (all replicas expose identical schemas)
        var mcpTools = await clients[0].ListToolsAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        foreach (var mcpTool in mcpTools)
        {
            var tool = CreatePoolToolDefinition(mcpTool.ProtocolTool, toolkit.Name, invoker);
            toolkit.AddTool(tool);
        }

        return toolkit;
    }

    private static ToolDefinition CreatePoolToolDefinition(
        Tool protocolTool, string kitName, McpToolInvoker invoker)
    {
        var parameters = ParseParameters(protocolTool.InputSchema);
        var toolName = protocolTool.Name;

        return new ToolDefinition
        {
            Name = toolName,
            Description = protocolTool.Description ?? string.Empty,
            Parameters = parameters,
            ExecutionMode = ToolExecutionMode.Mcp,
            Execute = (args, ct) => invoker.InvokeAsync(kitName, toolName, args, ct)
        };
    }

    private static IReadOnlyList<ToolParameter> ParseParameters(JsonElement inputSchema)
    {
        var parameters = new List<ToolParameter>();

        if (inputSchema.ValueKind != JsonValueKind.Object)
            return parameters;

        if (!inputSchema.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
            return parameters;

        var requiredSet = new HashSet<string>();
        if (inputSchema.TryGetProperty("required", out var requiredArr) &&
            requiredArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in requiredArr.EnumerateArray())
            {
                if (item.GetString() is { } name)
                    requiredSet.Add(name);
            }
        }

        foreach (var prop in properties.EnumerateObject())
        {
            var description = prop.Value.TryGetProperty("description", out var desc)
                ? desc.GetString() ?? string.Empty
                : string.Empty;

            var jsonType = prop.Value.TryGetProperty("type", out var typeEl)
                ? typeEl.GetString() ?? "string"
                : "string";

            parameters.Add(new ToolParameter(prop.Name, description, jsonType, IsRequired: requiredSet.Contains(prop.Name)));
        }

        return parameters;
    }

    private static IReadOnlyDictionary<string, object?> ToMcpArguments(IReadOnlyDictionary<string, object?> args)
    {
        var mcpArgs = new Dictionary<string, object?>(args.Count);

        foreach (var (key, value) in args)
            mcpArgs[key] = value;

        return mcpArgs;
    }

    private static string ExtractText(CallToolResult result)
    {
        if (result.Content is null || result.Content.Count == 0)
            return string.Empty;

        var textParts = result.Content
            .OfType<TextContentBlock>()
            .Select(c => c.Text);

        return string.Join("\n", textParts);
    }
}
