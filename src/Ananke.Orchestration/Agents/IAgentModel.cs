using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents;

/// <summary>
/// Abstraction over an LLM provider. Implement this interface to integrate any model backend.
/// Built-in implementations are provided in <c>Ananke.Orchestration.OpenAI</c> and
/// <c>Ananke.Orchestration.Anthropic</c>.
/// </summary>
public interface IAgentModel
{
    /// <summary>Sends <paramref name="request"/> to the model and returns the completion.</summary>
    Task<AgentResponse> GenerateAsync(AgentRequest request, CancellationToken ct = default);
}

/// <summary>
/// Optional extension of <see cref="IAgentModel"/> for providers that support streaming.
/// Consumers iterate <see cref="AgentStreamChunk"/> values for incremental text deltas,
/// with the final chunk carrying the fully assembled <see cref="AgentResponse"/>.
/// </summary>
public interface IStreamingAgentModel : IAgentModel
{
    /// <summary>Streams partial completion chunks as they arrive from the model.</summary>
    IAsyncEnumerable<AgentStreamChunk> GenerateStreamAsync(
        AgentRequest request,
        CancellationToken ct = default);
}
