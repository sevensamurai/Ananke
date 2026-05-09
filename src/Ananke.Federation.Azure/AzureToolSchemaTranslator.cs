using Ananke.Orchestration.Tools;
using System.Text.Json.Nodes;

namespace Ananke.Federation.Azure;

/// <summary>
/// Translates Ananke <see cref="Orchestration.Tools.ToolDefinition"/> instances into
/// JSON tool definitions for the Azure AI Agent Service create-agent API.
/// Returns <see cref="JsonArray"/> fragments that slot directly into the
/// <c>"tools"</c> property of a <c>DeclarativeAgentDefinition</c> request body.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ToolExecutionMode.PlatformNative"/> capabilities are passed through to the
/// platform API verbatim. Ananke does not gate-keep which capabilities are valid — the
/// platform rejects unknown values at deploy time. This avoids coupling Ananke's release
/// cadence to platform SDK changes.
/// </para>
/// <para>
/// A small set of user-friendly aliases (e.g. <c>"bing_search"</c> → <c>"bing_grounding"</c>)
/// are normalised automatically. Use <see cref="Capabilities"/> constants for discoverability.
/// </para>
/// </remarks>
public sealed class AzureToolSchemaTranslator
{
    /// <summary>
    /// Well-known platform capability identifiers for Azure AI Agent Service.
    /// These are <b>documentation helpers</b> for IntelliSense — the translator accepts
    /// any string and passes it through to the platform API.
    /// </summary>
    public static class Capabilities
    {
        /// <summary>Sandboxed Python code execution.</summary>
        public const string CodeInterpreter = "code_interpreter";

        /// <summary>Vector store-based file search.</summary>
        public const string FileSearch = "file_search";

        /// <summary>Bing web search grounding. Requires a Bing connection in the AI Foundry project.</summary>
        public const string BingSearch = "bing_search";

        /// <summary>Azure AI Search grounding. Requires a search index connection.</summary>
        public const string AzureAISearch = "azure_ai_search";

        /// <summary>
        /// Async tool execution via Azure Functions with Storage Queue binding.
        /// Requires an Azure Function connection in the AI Foundry project.
        /// </summary>
        public const string AzureFunction = "azure_function";

        /// <summary>SharePoint grounding (preview). Requires a SharePoint connection.</summary>
        public const string SharePoint = "sharepoint";
    }

    /// <summary>
    /// Maps user-friendly capability names to the wire-format type strings expected
    /// by the Azure AI Agent Service API. Capabilities not in this map are passed through as-is.
    /// </summary>
    private static readonly Dictionary<string, string> WireTypeAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bing_search"] = "bing_grounding",
        ["sharepoint"] = "sharepoint_grounding",
    };

    /// <summary>
    /// Translates a collection of Ananke tool definitions into a JSON array of
    /// Azure AI Agent Service tool objects.
    /// </summary>
    /// <param name="tools">The tool definitions to translate.</param>
    /// <returns>A <see cref="JsonArray"/> containing the translated tool JSON objects.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a <see cref="ToolExecutionMode.Local"/> tool is encountered,
    /// or when an <see cref="ToolExecutionMode.OpenApi"/> tool has no endpoint URI.
    /// </exception>
    public JsonArray Translate(IEnumerable<Orchestration.Tools.ToolDefinition> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        var result = new JsonArray();

        foreach (var tool in tools)
        {
            switch (tool.ExecutionMode)
            {
                case ToolExecutionMode.Local:
                    throw new InvalidOperationException(
                        $"Tool '{tool.Name}' uses Local execution mode and cannot be deployed to Azure AI Agent Service. " +
                        "Use .Callback(uri), .OpenApi(uri), or .PlatformNative() instead.");

                case ToolExecutionMode.Callback:
                case ToolExecutionMode.Mcp:
                    result.Add(ToFunctionToolJson(tool));
                    break;

                case ToolExecutionMode.OpenApi:
                    result.Add(ToOpenApiToolJson(tool));
                    break;

                case ToolExecutionMode.PlatformNative:
                    result.Add(ToPlatformNativeJson(tool));
                    break;
            }
        }

        return result;
    }

    private static JsonObject ToFunctionToolJson(Orchestration.Tools.ToolDefinition tool)
    {
        var fn = new JsonObject
        {
            ["name"] = tool.Name,
            ["description"] = tool.Description
        };

        if (tool.Parameters.Count > 0)
        {
            fn["parameters"] = JsonNode.Parse(tool.ParametersJsonSchema);
        }

        return new JsonObject
        {
            ["type"] = "function",
            ["function"] = fn
        };
    }

    /// <summary>
    /// Translates an <see cref="ToolExecutionMode.OpenApi"/> tool into the native
    /// Azure AI Agent Service <c>openapi</c> tool type. The platform reads the
    /// OpenAPI spec and handles HTTP invocation directly — no callback needed.
    /// </summary>
    private static JsonObject ToOpenApiToolJson(Orchestration.Tools.ToolDefinition tool)
    {
        var specUri = tool.Endpoint?.Uri
            ?? throw new InvalidOperationException(
                $"Tool '{tool.Name}' uses OpenApi execution mode but has no endpoint URI pointing to the OpenAPI spec.");

        var openApiDef = new JsonObject
        {
            ["name"] = tool.Name,
            ["spec"] = new JsonObject
            {
                ["url"] = specUri.AbsoluteUri
            },
            ["auth"] = new JsonObject
            {
                ["type"] = "anonymous"
            }
        };

        if (!string.IsNullOrWhiteSpace(tool.Description))
        {
            openApiDef["description"] = tool.Description;
        }

        return new JsonObject
        {
            ["type"] = "openapi",
            ["openapi"] = openApiDef
        };
    }

    /// <summary>
    /// Passes the <see cref="ToolDefinition.PlatformCapability"/> through as the JSON
    /// <c>type</c> field, applying alias normalisation where the user-facing name differs
    /// from the wire type (e.g. <c>"bing_search"</c> → <c>"bing_grounding"</c>).
    /// </summary>
    private static JsonObject ToPlatformNativeJson(Orchestration.Tools.ToolDefinition tool)
    {
        var capability = tool.PlatformCapability
            ?? throw new InvalidOperationException(
                $"Tool '{tool.Name}' is PlatformNative but has no PlatformCapability set.");

        var wireType = WireTypeAliases.TryGetValue(capability, out var alias)
            ? alias
            : capability.ToLowerInvariant();

        return new JsonObject { ["type"] = wireType };
    }
}
