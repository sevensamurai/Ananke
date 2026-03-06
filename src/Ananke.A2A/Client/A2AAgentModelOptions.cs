namespace Ananke.A2A.Client;

/// <summary>
/// Configuration for <see cref="A2AAgentModel"/> specifying the remote A2A agent endpoint
/// and client behavior.
/// </summary>
public sealed record A2AAgentModelOptions
{
    /// <summary>The base URL of the remote A2A agent endpoint.</summary>
    public required Uri AgentUrl { get; init; }

    /// <summary>
    /// Optional <see cref="HttpClient"/> instance for making A2A requests.
    /// When <c>null</c>, a new client is created internally.
    /// </summary>
    public HttpClient? HttpClient { get; init; }

    /// <summary>
    /// Media types the client is prepared to accept for response parts.
    /// Passed to the remote agent via <c>SendMessageConfiguration.AcceptedOutputModes</c>.
    /// Defaults to <c>["text/plain"]</c> when <c>null</c>.
    /// </summary>
    public IReadOnlyList<string>? AcceptedOutputModes { get; init; }
}
