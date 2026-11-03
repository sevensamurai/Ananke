using Ananke.Abstractions.Providers;
using Ananke.Orchestration.Tools;
using Ananke.Orchestration.Tools.Gating;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Ananke.MCP;

/// <summary>
/// Extension methods for <see cref="ToolKit"/> to import tools from an MCP server, or from an MCP tool
/// type in this process.
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
                return McpToolResults.From(result);
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

    /// <summary>
    /// Registers the tools an MCP tool type declares — each public method of <paramref name="target"/>'s
    /// type marked <see cref="McpServerToolAttribute"/> — run in this process, with no server between.
    /// </summary>
    /// <param name="toolkit">The toolkit to populate.</param>
    /// <param name="target">An instance of the tool type, whose methods the tools call.</param>
    /// <returns>The same <paramref name="toolkit"/> for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>Each tool is declared as an MCP server serves it</b> — its name, description and input schema
    /// are the SDK's for the method — so what a model is offered here is what a client of that server is
    /// offered. Use it where the tool type is in reach; <see cref="AddMcpServerToolsAsync"/> is for a
    /// server that is not.
    /// </para>
    /// <para>
    /// A method's arguments are bound to its own parameters. One an MCP server supplies itself, such as
    /// its request context, has nothing to bind to here. An exception from the method is an error result
    /// carrying its message.
    /// </para>
    /// </remarks>
    public static ToolKit AddMcpServerTools(this ToolKit toolkit, object target)
    {
        ArgumentNullException.ThrowIfNull(toolkit);
        ArgumentNullException.ThrowIfNull(target);

        var methods = target.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null);

        foreach (var method in methods)
        {
            var on = method.IsStatic ? null : target;
            var declared = McpServerTool.Create(method, on).ProtocolTool;

            // Bound with the options the declared schema was written with, and handed back as the
            // method returned it, so an MCP result type is read as one rather than as its JSON.
            var function = AIFunctionFactory.Create(method, on, new AIFunctionFactoryOptions
            {
                SerializerOptions = McpJsonUtilities.DefaultOptions,
                MarshalResult = (result, _, _) => new ValueTask<object?>(result)
            });

            toolkit.AddTool(new ToolDefinition
            {
                Name = declared.Name,
                Description = declared.Description ?? string.Empty,
                Parameters = ParseParameters(declared.InputSchema),
                Execute = async (args, ct) =>
                {
                    try
                    {
                        var answer = await function.InvokeAsync(new AIFunctionArguments(args.ToDictionary()), ct);
                        return ResultOf(answer);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        return ToolResult.Error((ex is TargetInvocationException { InnerException: { } inner } ? inner : ex).Message);
                    }
                }
            });
        }

        return toolkit;
    }

    /// <summary>
    /// A method's answer as a server's client would read it: an MCP result or content as
    /// <see cref="McpToolResults.From"/> reads it, a string as it is, anything else as its JSON.
    /// </summary>
    private static ToolResult ResultOf(object? answer) => answer switch
    {
        CallToolResult result => McpToolResults.From(result),
        ContentBlock block => McpToolResults.From(new CallToolResult { Content = [block] }),
        IEnumerable<ContentBlock> blocks => McpToolResults.From(new CallToolResult { Content = [.. blocks] }),
        null => ToolResult.Ok(string.Empty),
        string text => ToolResult.Ok(text),
        JsonElement { ValueKind: JsonValueKind.String } element => ToolResult.Ok(element.GetString() ?? string.Empty),
        JsonElement element => ToolResult.Ok(element.GetRawText()),
        _ => ToolResult.Ok(JsonSerializer.Serialize(answer, answer.GetType(), McpJsonUtilities.DefaultOptions))
    };

    internal static IReadOnlyList<ToolParameter> ParseParameters(JsonElement inputSchema)
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

            // An optional parameter's type may be a list, ["string", "null"]: the type is the one that is not null.
            var jsonType = !prop.Value.TryGetProperty("type", out var typeEl) ? "string"
                : typeEl.ValueKind == JsonValueKind.Array
                    ? typeEl.EnumerateArray().Select(t => t.GetString()).FirstOrDefault(t => t is not null and not "null") ?? "string"
                    : typeEl.GetString() ?? "string";

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
}
