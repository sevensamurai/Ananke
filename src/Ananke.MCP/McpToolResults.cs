using System.Text.Json;
using Ananke.Orchestration.Tools;
using ModelContextProtocol.Protocol;

namespace Ananke.MCP;

/// <summary>
/// An MCP tool's answer as a <see cref="ToolResult"/>, read the same way whether it came from a
/// server or from a tool type run in this process.
/// </summary>
internal static class McpToolResults
{
    /// <summary>
    /// <paramref name="result"/>'s text, one line per text block, as an error when it says
    /// <see cref="CallToolResult.IsError"/>. A result with no text block but structured content
    /// gives that content's JSON.
    /// </summary>
    public static ToolResult From(CallToolResult result)
    {
        var text = string.Join("\n", (result.Content ?? []).OfType<TextContentBlock>().Select(block => block.Text));

        if (text.Length == 0 && result.StructuredContent is { } structured)
            text = JsonSerializer.Serialize(structured);

        return result.IsError == true ? ToolResult.Error(text) : ToolResult.Ok(text);
    }
}
