namespace Ananke.Federation.Google.AgentRuntime;

/// <summary>
/// Seam for Gemini Enterprise Agent Platform — Agent Runtime lifecycle operations.
/// Allows production code to be tested against a fake without real Google Cloud calls.
/// </summary>
internal interface IAgentRuntimeClient
{
    /// <summary>
    /// Creates an agent in Agent Runtime and returns its platform resource name
    /// (e.g. <c>projects/my-project/locations/us-central1/agents/abc123</c>).
    /// </summary>
    Task<string> CreateAgentAsync(AgentDefinition definition, CancellationToken ct = default);

    /// <summary>
    /// Deletes a previously created agent by its platform resource name.
    /// </summary>
    Task DeleteAgentAsync(string resourceName, CancellationToken ct = default);
}
