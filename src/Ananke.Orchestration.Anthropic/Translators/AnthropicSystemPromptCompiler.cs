using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Translators;

namespace Ananke.Orchestration.Anthropic.Translators;

/// <summary>
/// Claude-idiomatic system-prompt compiler.
/// Follows Anthropic's recommendation to use XML-style structure markers for
/// clear section delineation.
/// </summary>
/// <remarks>
/// JSON-schema instructions are appended inline inside the system prompt rather than
/// sent as a separate message turn, because Claude treats the system prompt as the
/// authoritative behavioural contract.
/// </remarks>
public sealed class AnthropicSystemPromptCompiler : ISystemPromptCompiler
{
    /// <inheritdoc />
    public (string CompiledSystemPrompt, IReadOnlyList<AgentMessage> HintMessages) Compile(
        string? systemPrompt,
        string? jsonSchemaInstruction)
    {
        var parts = new List<string>(2);

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            parts.Add($"<instructions>\n{systemPrompt.Trim()}\n</instructions>");
        }

        if (!string.IsNullOrWhiteSpace(jsonSchemaInstruction))
        {
            parts.Add($"<output_format>\n{jsonSchemaInstruction.Trim()}\n</output_format>");
        }

        var compiled = parts.Count > 0
            ? string.Join("\n\n", parts)
            : string.Empty;

        return (compiled, Array.Empty<AgentMessage>());
    }
}
