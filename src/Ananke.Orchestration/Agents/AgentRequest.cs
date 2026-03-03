namespace Ananke.Orchestration.Agents;

public enum AgentRole
{
    System,
    User,
    Assistant,
    Tool
}

public sealed record AgentMessage
{
    public required AgentRole Role { get; init; }
    public string? Content { get; init; }
    public string? ToolCallId { get; init; }
    public IReadOnlyList<AgentToolCall>? ToolCalls { get; init; }

    public static AgentMessage System(string content) =>
        new() { Role = AgentRole.System, Content = content };

    public static AgentMessage User(string content) =>
        new() { Role = AgentRole.User, Content = content };

    public static AgentMessage Assistant(string content, IReadOnlyList<AgentToolCall>? toolCalls = null) =>
        new() { Role = AgentRole.Assistant, Content = content, ToolCalls = toolCalls };

    public static AgentMessage ToolResult(string toolCallId, string content) =>
        new() { Role = AgentRole.Tool, Content = content, ToolCallId = toolCallId };
}

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
