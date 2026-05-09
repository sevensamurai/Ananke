using Ananke.MCP;
using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;
using Ananke.Orchestration.Tools;
using ModelContextProtocol.Server;
using System.Text.Json;

// ReSharper disable once CheckNamespace — follows Microsoft's DI extension convention
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for <see cref="IMcpServerBuilder"/> to register Ananke tools and workflows
/// as MCP server capabilities.
/// </summary>
/// <remarks>
/// <b>Stdio transport warning:</b> When building a stdio-based MCP server, use
/// <c>Host.CreateEmptyApplicationBuilder(settings: null)</c> instead of <c>CreateDefaultBuilder</c>
/// to prevent console output from corrupting JSON-RPC messages. Never write to stdout in
/// stdio servers (including <c>Console.WriteLine</c>). See
/// <see href="https://modelcontextprotocol.io/docs/develop/build-server#c%23">MCP C# server guide</see>.
/// </remarks>
public static class AnankeMcpServerBuilderExtensions
{
    /// <summary>
    /// Registers all tools from an Ananke <see cref="ToolKit"/> as MCP server tools.
    /// Each <see cref="ToolDefinition"/> in the kit becomes an individually callable MCP tool.
    /// </summary>
    /// <example>
    /// <code>
    /// var toolkit = new ToolKit("stock")
    ///     .AddTool("get_price", "Gets the stock price", GetPrice, "symbol", "Ticker symbol");
    ///
    /// builder.Services.AddMcpServer(options =&gt; { ... })
    ///     .WithAnankeTools(toolkit);
    /// </code>
    /// </example>
    public static IMcpServerBuilder WithAnankeTools(
        this IMcpServerBuilder builder,
        ToolKit toolkit)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(toolkit);

        var mcpTools = toolkit.Tools.Values
            .Select(tool => (McpServerTool)new AnankeToolAdapter(tool))
            .ToList();

        return builder.WithTools(mcpTools);
    }

    /// <summary>
    /// Registers multiple Ananke <see cref="ToolKit"/> instances as MCP server tools.
    /// </summary>
    public static IMcpServerBuilder WithAnankeTools(
        this IMcpServerBuilder builder,
        params ToolKit[] toolkits)
    {
        ArgumentNullException.ThrowIfNull(builder);

        foreach (var toolkit in toolkits)
            builder.WithAnankeTools(toolkit);

        return builder;
    }

    /// <summary>
    /// Registers an Ananke <see cref="Workflow{TState}"/> as an MCP tool.
    /// When invoked, the workflow runs with the provided arguments mapped to the initial state.
    /// The final workflow state is returned as JSON.
    /// </summary>
    /// <typeparam name="TState">The workflow state type. Must be JSON-serializable.</typeparam>
    /// <param name="builder">The MCP server builder.</param>
    /// <param name="name">The MCP tool name (e.g. "run_triage").</param>
    /// <param name="description">Human-readable description of what the workflow does.</param>
    /// <param name="workflow">The workflow to expose.</param>
    /// <param name="stateFactory">
    /// Maps MCP tool arguments to the workflow's initial state.
    /// Receives the JSON arguments dictionary from the MCP call.
    /// </param>
    /// <param name="inputSchema">
    /// Optional JSON Schema for the tool's input. When <see langword="null"/>,
    /// a schema is auto-generated from <typeparamref name="TState"/>.
    /// </param>
    /// <example>
    /// <code>
    /// builder.Services.AddMcpServer(options =&gt; { ... })
    ///     .WithAnankeWorkflow("run_triage", "Runs the support triage workflow", workflow,
    ///         args =&gt; new TicketState
    ///         {
    ///             TicketId = args["ticketId"].GetString()!,
    ///             Description = args["description"].GetString()!
    ///         });
    /// </code>
    /// </example>
    public static IMcpServerBuilder WithAnankeWorkflow<TState>(
        this IMcpServerBuilder builder,
        string name,
        string description,
        Workflow<TState> workflow,
        Func<IReadOnlyDictionary<string, JsonElement>, TState> stateFactory,
        JsonElement? inputSchema = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(stateFactory);

        var tool = new WorkflowToolAdapter<TState>(name, description, workflow, stateFactory, inputSchema);
        return builder.WithTools([tool]);
    }
}
