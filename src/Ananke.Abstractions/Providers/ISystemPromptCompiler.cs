using Ananke.Abstractions.Agents;

namespace Ananke.Abstractions.Providers;

/// <summary>
/// Fuses a system prompt and an optional JSON schema instruction into the format
/// expected by a specific provider's API.
/// </summary>
/// <remarks>
/// The canonical base implementation is <see cref="SystemPromptBuilder.Default"/>. Provider
/// implementations may override how the schema instruction is embedded (e.g. appended
/// inline, placed in a separate <c>system</c> block, etc.).
/// </remarks>
public interface ISystemPromptCompiler
{
    /// <summary>
    /// Builds the provider-idiomatic system prompt, optionally embedding a
    /// JSON-schema instruction for structured output.
    /// </summary>
    /// <param name="systemPrompt">
    /// The canonical system prompt text. May be <see langword="null"/> or empty when
    /// the agent has no user-defined system instructions.
    /// </param>
    /// <param name="jsonSchemaInstruction">
    /// An optional instruction fragment that directs the model to respond in a
    /// specific JSON format. When <see langword="null"/>, the compiled output contains
    /// only the system prompt.
    /// </param>
    /// <returns>
    /// A tuple of <c>(CompiledSystemPrompt, HintMessages)</c>. <c>HintMessages</c>
    /// carries any additional provider messages (e.g. a user/assistant turn pair that
    /// Google requires to simulate a system turn), or an empty list when unused.
    /// </returns>
    (string CompiledSystemPrompt, IReadOnlyList<AgentMessage> HintMessages) Compile(
        string? systemPrompt,
        string? jsonSchemaInstruction);
}
