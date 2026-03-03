namespace Ananke.Orchestration.Agents;

public sealed record AgentToolCall(string Id, string FunctionName, string Arguments);

public sealed record AgentResponse
{
    public string? Text { get; init; }
    public IReadOnlyList<AgentToolCall>? ToolCalls { get; init; }
    public bool RequiresAction => ToolCalls is { Count: > 0 };
}
