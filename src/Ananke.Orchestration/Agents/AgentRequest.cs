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

    /// <summary>
    /// Multimodal content parts. When set, <see cref="Content"/> is computed by
    /// concatenating text from any <see cref="TextPart"/> entries.
    /// </summary>
    public IReadOnlyList<ContentPart>? Parts { get; init; }

    private readonly string? _content;

    /// <summary>
    /// Text content of the message. When <see cref="Parts"/> is set, returns the
    /// concatenated text from <see cref="TextPart"/> entries; otherwise returns
    /// the value set directly.
    /// </summary>
    public string? Content
    {
        get
        {
            if (Parts is not { Count: > 0 })
                return _content;

            var joined = string.Concat(Parts.OfType<TextPart>().Select(p => p.Text));
            return joined.Length > 0 ? joined : null;
        }
        init => _content = value;
    }

    public string? ToolCallId { get; init; }
    public IReadOnlyList<AgentToolCall>? ToolCalls { get; init; }

    public static AgentMessage System(string content) =>
        new() { Role = AgentRole.System, Content = content };

    public static AgentMessage User(string content) =>
        new() { Role = AgentRole.User, Content = content };

    public static AgentMessage User(IReadOnlyList<ContentPart> parts) =>
        new() { Role = AgentRole.User, Parts = parts };

    public static AgentMessage UserAudio(byte[] data, string mimeType) =>
        new() { Role = AgentRole.User, Parts = [new AudioPart(data, mimeType)] };

    public static AgentMessage UserImage(byte[] data, string mimeType, string? text = null) =>
        text is not null
            ? new() { Role = AgentRole.User, Parts = [new TextPart(text), new ImagePart { Data = data, MimeType = mimeType }] }
            : new() { Role = AgentRole.User, Parts = [new ImagePart { Data = data, MimeType = mimeType }] };

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
