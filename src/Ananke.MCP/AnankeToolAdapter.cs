using System.Text.Json;
using System.Text.Json.Nodes;
using Ananke.Orchestration.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Ananke.MCP;

/// <summary>
/// Adapts an Ananke <see cref="ToolDefinition"/> into an <see cref="McpServerTool"/>
/// so it can be served via the MCP protocol.
/// </summary>
internal sealed class AnankeToolAdapter : McpServerTool
{
    private readonly ToolDefinition _tool;
    private readonly Tool _protocolTool;

    internal AnankeToolAdapter(ToolDefinition tool)
    {
        _tool = tool;
        _protocolTool = new Tool
        {
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = JsonElementFromNode(BuildInputSchema(tool))
        };
    }

    public override Tool ProtocolTool => _protocolTool;

    public override IReadOnlyList<object> Metadata => [];

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> request,
        CancellationToken ct = default)
    {
        var args = ExtractArguments(request.Params?.Arguments);
        var result = await _tool.ExecuteAsync(args, ct);

        return new CallToolResult
        {
            IsError = result.IsError,
            Content = [new TextContentBlock { Text = result.Value }]
        };
    }

    private static JsonObject BuildInputSchema(ToolDefinition tool)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var param in tool.Parameters)
        {
            properties[param.Name] = new JsonObject
            {
                ["type"] = param.JsonType,
                ["description"] = param.Description
            };
            required.Add(param.Name);
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required
        };
    }

    private static IReadOnlyDictionary<string, object?> ExtractArguments(
        IDictionary<string, JsonElement>? mcpArgs)
    {
        if (mcpArgs is null || mcpArgs.Count == 0)
            return new Dictionary<string, object?>();

        var dict = new Dictionary<string, object?>(mcpArgs.Count);
        foreach (var (key, element) in mcpArgs)
            dict[key] = element.Clone();

        return dict;
    }

    internal static JsonElement JsonElementFromNode(JsonNode node)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(node);
        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.Clone();
    }
}
