using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents;

public sealed record AgentTool(string Name, string Description, string ParametersJsonSchema);

public sealed record AgentResponseFormat(string SchemaName, string JsonSchema, bool Strict = true);

public sealed record AgentRequest
{
    public string? SystemPrompt { get; init; }
    public required IReadOnlyList<AgentMessage> Messages { get; init; }
    public IReadOnlyList<AgentTool>? Tools { get; init; }
    public AgentResponseFormat? ResponseFormat { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// When <c>true</c>, the provider stores the completion so it appears in platform logs
    /// (e.g. <see href="https://platform.openai.com/logs"/>). Default is <c>true</c>.
    /// </summary>
    public bool StoreCompletions { get; init; } = true;
}
