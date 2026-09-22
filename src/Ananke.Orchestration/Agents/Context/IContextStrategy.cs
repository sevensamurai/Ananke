using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents.Context;

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
    /// Filters, compacts, or summarizes the message list to fit within constraints, and reports
    /// what it withheld.
    /// </summary>
    /// <param name="messages">The full message history to compact.</param>
    /// <param name="systemPrompt">
    /// The system prompt that will accompany the messages (if any).
    /// Implementations should account for its token cost in their budget.
    /// </param>
    /// <param name="budget">
    /// The allocation for this assembly and the selected model's real window. Pass
    /// <see cref="ContextBudget.Unspecified"/> when neither is known — the implementation then
    /// applies whatever it was configured with. Use <see cref="ContextBudget.Resolve"/> rather than
    /// reading the members directly, so allocation and capacity are reconciled the same way
    /// everywhere.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// What the model should see, plus how much was withheld and why. Compaction is lossy, and a
    /// return type that cannot say what it dropped makes that loss unauditable.
    /// </returns>
    Task<ContextProjection> ApplyAsync(
        IReadOnlyList<AgentMessage> messages,
        string? systemPrompt,
        ContextBudget budget,
        CancellationToken ct = default);
}
