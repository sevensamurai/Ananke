namespace Ananke.Abstractions.Agents;

/// <summary>Describes a tool the model can invoke during generation.</summary>
public sealed record AgentTool(string Name, string Description, string ParametersJsonSchema);

/// <summary>Constrains model output to a specific JSON schema.</summary>
public sealed record AgentResponseFormat(string SchemaName, string JsonSchema, bool Strict = true);

/// <summary>
/// A request to an <see cref="IAgentModel"/>. Contains the conversation messages,
/// optional tools, optional structured output format, and provider metadata.
/// </summary>
public sealed record AgentRequest
{
    /// <summary>System prompt prepended to the conversation.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>The conversation messages to send to the model.</summary>
    public required IReadOnlyList<AgentMessage> Messages { get; init; }

    /// <summary>Tools the model may invoke. When <see langword="null"/>, no tool calling is enabled.</summary>
    public IReadOnlyList<AgentTool>? Tools { get; init; }

    /// <summary>When set, constrains the model to output JSON matching the specified schema.</summary>
    public AgentResponseFormat? ResponseFormat { get; init; }

    /// <summary>Arbitrary key-value metadata forwarded to the provider.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// When <c>true</c>, the provider stores the completion so it appears in platform logs
    /// (e.g. <see href="https://platform.openai.com/logs"/>). Default is <c>false</c>.
    /// </summary>
    public bool StoreCompletions { get; init; }
}
