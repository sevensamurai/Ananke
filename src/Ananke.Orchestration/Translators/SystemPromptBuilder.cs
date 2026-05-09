using System.Text;
using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Translators;

/// <summary>
/// Shared base helper for canonical system-prompt + JSON-schema-instruction fusion.
/// Provider-specific <see cref="ISystemPromptCompiler"/> implementations use this
/// to assemble the common text portion before applying any provider-idiomatic
/// adjustments.
/// </summary>
public static class SystemPromptBuilder
{
    /// <summary>
    /// Builds the canonical prompt text by appending <paramref name="jsonSchemaInstruction"/>
    /// to <paramref name="systemPrompt"/> (separated by a blank line) when both are present.
    /// </summary>
    /// <param name="systemPrompt">Base system prompt. May be <see langword="null"/>.</param>
    /// <param name="jsonSchemaInstruction">
    /// JSON schema instruction fragment. May be <see langword="null"/>.
    /// </param>
    /// <returns>The fused prompt string, never <see langword="null"/>.</returns>
    public static string Fuse(string? systemPrompt, string? jsonSchemaInstruction)
    {
        var hasSystem = !string.IsNullOrWhiteSpace(systemPrompt);
        var hasSchema = !string.IsNullOrWhiteSpace(jsonSchemaInstruction);

        if (!hasSystem && !hasSchema)
            return string.Empty;

        if (hasSystem && !hasSchema)
            return systemPrompt!;

        if (!hasSystem && hasSchema)
            return jsonSchemaInstruction!;

        var sb = new StringBuilder(capacity: systemPrompt!.Length + jsonSchemaInstruction!.Length + 2);
        sb.Append(systemPrompt);
        sb.AppendLine();
        sb.AppendLine();
        sb.Append(jsonSchemaInstruction);
        return sb.ToString();
    }

    /// <summary>
    /// Default <see cref="ISystemPromptCompiler"/> implementation: fuses the prompt and
    /// schema instruction with <see cref="Fuse"/> and returns no hint messages.
    /// Suitable for providers that accept a plain system prompt string (OpenAI, Anthropic).
    /// </summary>
    public sealed class Default : ISystemPromptCompiler
    {
        /// <summary>A shared, stateless instance.</summary>
        public static readonly Default Instance = new();

        /// <inheritdoc />
        public (string CompiledSystemPrompt, IReadOnlyList<AgentMessage> HintMessages) Compile(
            string? systemPrompt,
            string? jsonSchemaInstruction) =>
            (Fuse(systemPrompt, jsonSchemaInstruction), []);
    }

    /// <summary>
    /// Pass-through <see cref="IJsonSchemaTranslator"/> for providers that accept
    /// standard JSON Schema without any dialect translation.
    /// </summary>
    public sealed class PassThroughJsonSchemaTranslator : IJsonSchemaTranslator
    {
        /// <summary>A shared, stateless instance.</summary>
        public static readonly PassThroughJsonSchemaTranslator Instance = new();

        /// <inheritdoc />
        public object Translate(IReadOnlyDictionary<string, object> schema) => schema;
    }
}
