using Ananke.Abstractions.Providers;
using OpenAI.Chat;

namespace Ananke.Orchestration.OpenAI.Translators;

/// <summary>
/// Translates <see cref="ProviderTool"/> instances into OpenAI
/// <see cref="ChatTool"/> objects suitable for the Chat Completions API.
/// </summary>
/// <remarks>
/// <see cref="ToolExecutionMode.Local"/> tools are rejected at translation time because
/// the OpenAI API cannot invoke local callbacks. All remote modes (Callback, MCP, OpenApi,
/// PlatformNative) are mapped to standard function tools; there is no built-in tool type
/// exposed by the Chat Completions API.
/// </remarks>
public sealed class OpenAIToolSchemaTranslator : IToolSchemaTranslator
{
    /// <inheritdoc />
    /// <returns>An <see cref="IReadOnlyList{T}"/> of <see cref="ChatTool"/>.</returns>
    public object Translate(IEnumerable<ProviderTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        var result = new List<ChatTool>();

        foreach (var tool in tools)
        {
            if (tool.ExecutionMode == ToolExecutionMode.Local)
                throw new InvalidOperationException(
                    $"Tool '{tool.Name}' uses Local execution mode and cannot be sent to the OpenAI API. " +
                    "Use .Callback(uri), .Mcp(uri), .OpenApi(uri), or .PlatformNative() instead.");

            result.Add(ChatTool.CreateFunctionTool(
                tool.Name,
                tool.Description,
                BinaryData.FromString(tool.ParametersJsonSchema)));
        }

        return result.AsReadOnly();
    }
}
