using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Providers;

namespace Ananke.Orchestration.Google.Translators;

/// <summary>
/// Gemini-idiomatic system-prompt compiler.
/// The Gemini Developer API accepts a <c>system_instruction</c> field; for structured
/// output the JSON-schema instruction is appended inline. No separate hint messages
/// are needed for the developer API; the base class pattern is used.
/// </summary>
public sealed class GeminiSystemPromptCompiler : ISystemPromptCompiler
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
