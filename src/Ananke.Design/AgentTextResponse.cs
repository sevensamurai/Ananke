namespace Ananke.Design;

/// <summary>
/// Default structured response type for agent jobs that produce plain text output.
/// Use with <c>AgentJobFactory.Create&lt;TState, AgentTextResponse&gt;()</c> when the
/// agent returns unstructured text rather than a domain-specific schema.
/// </summary>
public sealed record AgentTextResponse
{
    /// <summary>The text content returned by the agent.</summary>
    public string? Text { get; init; }
}
