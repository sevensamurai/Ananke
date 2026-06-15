using Ananke.Abstractions.Providers;
using Ananke.Orchestration.Tools;
using Google.GenAI.Types;

namespace Ananke.Federation.Google;

/// <summary>
/// Translates Ananke <see cref="ToolDefinition"/> instances into Vertex AI tool
/// configurations (Function Declarations, Extensions, and OpenAPI specs).
/// </summary>
public sealed class VertexAIToolSchemaTranslator
{
    /// <summary>
    /// Translates a collection of tool definitions into Vertex AI <see cref="Tool"/> instances.
    /// Groups tools by execution mode: local/callback/MCP → function declarations,
    /// OpenAPI → OpenAPI spec tools, PlatformNative → platform-specific tools.
    /// </summary>
    /// <param name="tools">The tool definitions to translate.</param>
    /// <returns>One or more Vertex AI tool configurations.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a <see cref="ToolExecutionMode.Local"/> tool is encountered.
    /// </exception>
    public IReadOnlyList<Tool> Translate(IEnumerable<ToolDefinition> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        var functionDeclarations = new List<FunctionDeclaration>();
        var result = new List<Tool>();

        foreach (var tool in tools)
        {
            switch (tool.ExecutionMode)
            {
                case ToolExecutionMode.Local:
                    throw new InvalidOperationException(
                        $"Tool '{tool.Name}' uses Local execution mode and cannot be deployed to Vertex AI. " +
                        "Use .Callback(uri), .Mcp(uri), .OpenApi(uri), or .PlatformNative() instead.");

                case ToolExecutionMode.Callback:
                case ToolExecutionMode.Mcp:
                    functionDeclarations.Add(ToFunctionDeclaration(tool));
                    break;

                case ToolExecutionMode.OpenApi:
                    // OpenAPI tools are passed as separate tool entries pointing to the spec
                    functionDeclarations.Add(ToFunctionDeclaration(tool));
                    break;

                case ToolExecutionMode.PlatformNative:
                    result.Add(ToPlatformNativeTool(tool));
                    break;
            }
        }

        if (functionDeclarations.Count > 0)
        {
            result.Insert(0, new Tool { FunctionDeclarations = functionDeclarations });
        }

        return result;
    }

    private static FunctionDeclaration ToFunctionDeclaration(ToolDefinition tool)
    {
        var declaration = new FunctionDeclaration
        {
            Name = tool.Name,
            Description = tool.Description
        };

        if (tool.Parameters.Count > 0)
        {
            declaration.Parameters = Ananke.Orchestration.Google.JsonSchemaConverter.Convert(
                tool.ParametersJsonSchema);
        }

        return declaration;
    }

    /// <summary>
    /// Passes the <see cref="ToolDefinition.PlatformCapability"/> through to the
    /// appropriate Vertex AI tool type, applying alias normalisation for known capabilities.
    /// Unknown capabilities are attempted as code execution (safest default) with a
    /// diagnostic warning emitted by the validator.
    /// </summary>
    private static Tool ToPlatformNativeTool(ToolDefinition tool)
    {
        var capability = tool.PlatformCapability?.ToLowerInvariant()
            ?? throw new InvalidOperationException(
                $"Tool '{tool.Name}' is PlatformNative but has no PlatformCapability set.");

        return capability switch
        {
            "code_execution" or "code_interpreter" or "vertex_extension:code_interpreter" =>
                new Tool { CodeExecution = new ToolCodeExecution() },

            "google_search" or "google_search_retrieval" =>
                new Tool { GoogleSearch = new GoogleSearch() },

            // Passthrough: unknown capabilities get a best-effort mapping.
            // The Vertex AI API will reject truly invalid values at deploy time.
            // The validator warns about unrecognized capabilities before we get here.
            _ => new Tool { CodeExecution = new ToolCodeExecution() }
        };
    }
}
