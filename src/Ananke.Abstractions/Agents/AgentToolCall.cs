namespace Ananke.Abstractions.Agents;

/// <summary>
/// Represents a tool call requested by the model during generation.
/// </summary>
public sealed record AgentToolCall(string Id, string FunctionName, string Arguments);
