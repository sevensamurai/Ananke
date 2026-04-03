using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents;

/// <summary>
/// Controls how conversation history is managed before being sent to the model.
/// Applied by <see cref="AgentJob{TState, TResponse}"/> and
/// <see cref="StreamingChatWorkflow"/> when the message list may exceed the
/// model's context window.
/// </summary>
/// <remarks>
/// Implementations should preserve message semantics: never reorder messages,
/// always keep the most recent user message, and account for the system prompt
/// token cost in their budget calculations.
/// </remarks>
public interface IContextStrategy
{
    /// <summary>
    /// Filters, compacts, or summarizes the message list to fit within constraints.
    /// The system prompt (if any) is passed separately so implementations can account
    /// for its token cost. Returns the (possibly shorter) message list to send.
    /// </summary>
    /// <param name="messages">The full message history to compact.</param>
    /// <param name="systemPrompt">
    /// The system prompt that will accompany the messages (if any).
    /// Implementations should account for its token cost in their budget.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The compacted message list. May be the same instance if no compaction was needed.</returns>
    Task<IReadOnlyList<AgentMessage>> ApplyAsync(
        IReadOnlyList<AgentMessage> messages,
        string? systemPrompt,
        CancellationToken ct = default);
}
