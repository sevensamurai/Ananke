using System.Text.Json.Nodes;
using Ananke.Abstractions.Providers;
using Ananke.Orchestration.Tools;

namespace Ananke.Federation.Anthropic;

/// <summary>
/// Translates Ananke <see cref="ToolDefinition"/> instances into JSON tool definitions
/// for the Claude tool_use API. Returns <see cref="JsonArray"/> fragments for the
/// <c>"tools"</c> property of a create-agent request body.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ToolExecutionMode.PlatformNative"/> capabilities are passed through verbatim.
/// Claude's built-in tools (web_search, code_execution, computer_use, text_editor, bash)
/// use a different schema from custom tools — the translator handles both forms.
/// </para>
/// </remarks>
public sealed class ClaudeToolSchemaTranslator
{
    /// <summary>
    /// Well-known platform capability identifiers for Claude.
    /// These are <b>documentation helpers</b> for IntelliSense — the translator accepts
    /// any string and passes it through to the platform API.
    /// </summary>
    public static class Capabilities
    {
        /// <summary>Web search via Anthropic's built-in search tool.</summary>
        public const string WebSearch = "web_search";

        /// <summary>Sandboxed code execution.</summary>
        public const string CodeExecution = "code_execution";

        /// <summary>Computer use (screenshot + mouse/keyboard control).</summary>
        public const string ComputerUse = "computer_use";

        /// <summary>Text editor tool (view, create, edit files).</summary>
        public const string TextEditor = "text_editor";

        /// <summary>Bash shell execution.</summary>
        public const string Bash = "bash";
    }

    /// <summary>
    /// Claude built-in tools use a different JSON shape than custom tools.
    /// These are the capabilities that produce <c>{"type": "&lt;capability&gt;"}</c>
    /// without a function definition.
    /// </summary>
    private static readonly HashSet<string> BuiltInCapabilities = new(StringComparer.OrdinalIgnoreCase)
    {
        "web_search", "code_execution", "computer_use",
        "text_editor", "bash"
    };

    /// <summary>
    /// Translates a collection of Ananke tool definitions into a JSON array of
    /// Claude tool objects.
    /// </summary>
    /// <param name="tools">The tool definitions to translate.</param>
    /// <returns>A <see cref="JsonArray"/> containing the translated tool JSON objects.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a <see cref="ToolExecutionMode.Local"/> tool is encountered.
    /// </exception>
    public JsonArray Translate(IEnumerable<ToolDefinition> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        var result = new JsonArray();

        foreach (var tool in tools)
        {
            switch (tool.ExecutionMode)
            {
                case ToolExecutionMode.Local:
                    throw new InvalidOperationException(
                        $"Tool '{tool.Name}' uses Local execution mode and cannot be deployed to Claude. " +
                        "Use .Callback(uri), .Mcp(uri), .OpenApi(uri), or .PlatformNative() instead.");

                case ToolExecutionMode.Callback:
                case ToolExecutionMode.Mcp:
                case ToolExecutionMode.OpenApi:
                    result.Add(ToCustomToolJson(tool));
                    break;

                case ToolExecutionMode.PlatformNative:
                    result.Add(ToPlatformNativeJson(tool));
                    break;
            }
        }

        return result;
    }

    private static JsonObject ToCustomToolJson(ToolDefinition tool)
    {
        var toolObj = new JsonObject
        {
            ["name"] = tool.Name,
            ["description"] = tool.Description
        };

        if (tool.Parameters.Count > 0)
        {
            toolObj["input_schema"] = JsonNode.Parse(tool.ParametersJsonSchema);
        }
        else
        {
            toolObj["input_schema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject()
            };
        }

        return toolObj;
    }

    /// <summary>
    /// Passes the <see cref="ToolDefinition.PlatformCapability"/> through as the tool type.
    /// Built-in Claude tools use <c>{"type": "&lt;capability&gt;"}</c> format.
    /// Unknown capabilities are passed through — Claude API validates.
    /// </summary>
    private static JsonObject ToPlatformNativeJson(ToolDefinition tool)
    {
        var capability = tool.PlatformCapability
            ?? throw new InvalidOperationException(
                $"Tool '{tool.Name}' is PlatformNative but has no PlatformCapability set.");

        if (BuiltInCapabilities.Contains(capability))
        {
            // Built-in tools use a simple type declaration
            return new JsonObject { ["type"] = capability.ToLowerInvariant() };
        }

        // Unknown capability — pass through as type (Claude API will validate)
        return new JsonObject { ["type"] = capability.ToLowerInvariant() };
    }
}
