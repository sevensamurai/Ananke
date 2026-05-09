using Google.GenAI.Types;

namespace Ananke.Federation.Google.AgentRuntime;

/// <summary>
/// Intermediate representation of an agent configuration to be submitted to Agent Runtime.
/// Built by <see cref="VertexAIDeployer"/> from a translated manifest, then passed to
/// <see cref="IAgentRuntimeClient.CreateAgentAsync"/>.
/// </summary>
internal sealed record AgentDefinition
{
    /// <summary>Display name for the agent resource (typically the workflow + job name).</summary>
    public required string DisplayName { get; init; }

    /// <summary>Gemini model identifier resolved by <see cref="VertexAIModelMapper"/>.</summary>
    public required string Model { get; init; }

    /// <summary>System instructions compiled by <see cref="VertexAISystemPromptCompiler"/>.</summary>
    public required string SystemInstructions { get; init; }

    /// <summary>Tool declarations translated by <see cref="Ananke.Orchestration.Google.Translators.GeminiToolSchemaTranslator"/>.</summary>
    public IReadOnlyList<Tool> Tools { get; init; } = [];
}
