using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Translators;

namespace Ananke.Orchestration.OpenAI.Translators;

/// <summary>
/// OpenAI-idiomatic system-prompt compiler.
/// The system prompt and any JSON-schema instruction are fused into a single string
/// that is passed as the <c>system</c> message. No additional hint messages are needed.
/// </summary>
public sealed class OpenAISystemPromptCompiler : ISystemPromptCompiler
{
    /// <inheritdoc />
    public (string CompiledSystemPrompt, IReadOnlyList<AgentMessage> HintMessages) Compile(
        string? systemPrompt,
        string? jsonSchemaInstruction)
    {
        var compiled = SystemPromptBuilder.Fuse(systemPrompt, jsonSchemaInstruction);
        return (compiled, Array.Empty<AgentMessage>());
    }
}
