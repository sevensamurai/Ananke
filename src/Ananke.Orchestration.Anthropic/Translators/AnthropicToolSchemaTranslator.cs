using System.Text.Json.Nodes;
using Ananke.Abstractions.Providers;

namespace Ananke.Orchestration.Anthropic.Translators;

/// <summary>
/// Translates <see cref="ProviderTool"/> instances into JSON tool definitions
/// for the Anthropic Messages API. Returns a <see cref="JsonArray"/> suitable for the
/// <c>"tools"</c> property of a create-message request body.
/// </summary>
/// <remarks>
/// Claude's built-in tools (web_search, code_execution, computer_use, text_editor, bash)
/// use <c>{"type": "&lt;capability&gt;"}</c>; custom tools carry a full function schema.
/// <see cref="ToolExecutionMode.Local"/> tools are rejected at translation time.
/// </remarks>
public sealed class AnthropicToolSchemaTranslator : IToolSchemaTranslator
{
    private static readonly HashSet<string> BuiltInCapabilities =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "web_search", "code_execution", "computer_use", "text_editor", "bash"
        };

    /// <inheritdoc />
    /// <returns>A <see cref="JsonArray"/> of Anthropic tool objects.</returns>
    public object Translate(IEnumerable<ProviderTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        var result = new JsonArray();

        foreach (var tool in tools)
        {
            switch (tool.ExecutionMode)
            {
                case ToolExecutionMode.Local:
                    throw new InvalidOperationException(
                        $"Tool '{tool.Name}' uses Local execution mode and cannot be sent to Anthropic. " +
                        "Use .Callback(uri), .Mcp(uri), .OpenApi(uri), or .PlatformNative() instead.");

                case ToolExecutionMode.PlatformNative:
                    result.Add(ToPlatformNativeJson(tool));
                    break;

                default:
                    result.Add(ToCustomToolJson(tool));
                    break;
            }
        }

        return result;
    }

    private static JsonObject ToCustomToolJson(ProviderTool tool)
    {
        var obj = new JsonObject
        {
            ["name"] = tool.Name,
            ["description"] = tool.Description,
            ["input_schema"] = JsonNode.Parse(tool.ParametersJsonSchema)
        };

        return obj;
    }

    private static JsonObject ToPlatformNativeJson(ProviderTool tool)
    {
        var capability = tool.PlatformCapability
            ?? throw new InvalidOperationException(
                $"Tool '{tool.Name}' is PlatformNative but has no PlatformCapability set.");

        return new JsonObject { ["type"] = capability.ToLowerInvariant() };
    }
}
