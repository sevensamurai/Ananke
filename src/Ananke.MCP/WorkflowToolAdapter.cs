using Ananke.Orchestration;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ananke.MCP;

/// <summary>
/// Exposes an Ananke <see cref="Workflow{TState}"/> as an MCP tool.
/// When invoked, the workflow runs with the provided initial state and returns
/// the final state as JSON.
/// </summary>
/// <typeparam name="TState">The workflow state type. Must be JSON-serializable.</typeparam>
internal sealed class WorkflowToolAdapter<TState> : McpServerTool
{
    private readonly Workflow<TState> _workflow;
    private readonly Tool _protocolTool;
    private readonly Func<IReadOnlyDictionary<string, JsonElement>, TState> _stateFactory;

    private static readonly JsonSerializerOptions ResultJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal WorkflowToolAdapter(
        string name,
        string description,
        Workflow<TState> workflow,
        Func<IReadOnlyDictionary<string, JsonElement>, TState> stateFactory,
        JsonElement? inputSchema = null)
    {
        _workflow = workflow;
        _stateFactory = stateFactory;
        _protocolTool = new Tool
        {
            Name = name,
            Description = description,
            InputSchema = inputSchema ?? BuildSchemaFromType()
        };
    }

    public override Tool ProtocolTool => _protocolTool;

    public override IReadOnlyList<object> Metadata => [];

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken ct = default)
    {
        var rawArgs = request.Params?.Arguments;
        IReadOnlyDictionary<string, JsonElement> args = rawArgs is not null
            ? new Dictionary<string, JsonElement>(rawArgs)
            : new Dictionary<string, JsonElement>();
        var initialState = _stateFactory(args);

        var execution = await _workflow.RunAsync(initialState, ct);

        if (execution.Status == ExecutionStatus.Faulted)
        {
            var errorMessage = execution.History.LastOrDefault()?.Error ?? "Workflow faulted.";
            return new CallToolResult
            {
                IsError = true,
                Content = [new TextContentBlock { Text = errorMessage }]
            };
        }

        var resultJson = JsonSerializer.Serialize(execution.State, ResultJsonOptions);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = resultJson }]
        };
    }

    private static JsonElement BuildSchemaFromType()
    {
        var schema = Ananke.Orchestration.Agents.JsonSchemaGenerator.GenerateForType(typeof(TState));
        var node = JsonSerializer.SerializeToNode(schema) ?? new JsonObject { ["type"] = "object" };
        return AnankeToolAdapter.JsonElementFromNode(node);
    }
}
