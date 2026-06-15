using Ananke.Abstractions.Providers;
using Google.GenAI.Types;

namespace Ananke.Orchestration.Google.Translators;

/// <summary>
/// Translates <see cref="ProviderTool"/> instances into Google GenAI
/// <see cref="Tool"/> configurations (function declarations, code execution, Google Search).
/// </summary>
/// <remarks>
/// Groups tools by execution mode:
/// <list type="bullet">
///   <item>Local execution is rejected — agents must use Callback, MCP, OpenApi, or PlatformNative.</item>
///   <item>Callback / MCP / OpenApi → function declarations bundled in a single <see cref="Tool"/>.</item>
///   <item>PlatformNative → mapped to the appropriate Vertex AI / Gemini built-in tool.</item>
/// </list>
/// Returns <see cref="IReadOnlyList{T}"/> of <see cref="Tool"/> as an <see langword="object"/>.
/// </remarks>
public sealed class GeminiToolSchemaTranslator : IToolSchemaTranslator
{
    /// <inheritdoc />
    /// <returns>An <see cref="IReadOnlyList{T}"/> of <see cref="Tool"/> objects.</returns>
    public object Translate(IEnumerable<ProviderTool> tools)
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
                        $"Tool '{tool.Name}' uses Local execution mode and cannot be deployed to Gemini/Vertex AI. " +
                        "Use .Callback(uri), .Mcp(uri), .OpenApi(uri), or .PlatformNative() instead.");

                case ToolExecutionMode.Callback:
                case ToolExecutionMode.Mcp:
                case ToolExecutionMode.OpenApi:
                    functionDeclarations.Add(ToFunctionDeclaration(tool));
                    break;

                case ToolExecutionMode.PlatformNative:
                    result.Add(ToPlatformNativeTool(tool));
                    break;
            }
        }

        if (functionDeclarations.Count > 0)
            result.Insert(0, new Tool { FunctionDeclarations = functionDeclarations });

        return result.AsReadOnly();
    }

    private static FunctionDeclaration ToFunctionDeclaration(ProviderTool tool)
    {
        var declaration = new FunctionDeclaration
        {
            Name = tool.Name,
            Description = tool.Description
        };

        var schema = tool.ParametersJsonSchema;
        if (!string.IsNullOrWhiteSpace(schema) && schema.Trim() != "{}")
            declaration.Parameters = JsonSchemaConverter.Convert(schema);

        return declaration;
    }

    private static Tool ToPlatformNativeTool(ProviderTool tool)
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

            _ => new Tool { CodeExecution = new ToolCodeExecution() }
        };
    }
}
