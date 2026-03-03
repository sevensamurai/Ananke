using Ananke.Orchestration.Agents;

namespace Ananke.Orchestration.Memory;

/// <summary>
/// Persistent conversation history for multi-turn agent interactions.
/// Scoped per session (typically a workflow execution) so each execution
/// has isolated conversation history.
/// </summary>
/// <remarks>
/// Built-in implementations: <see cref="InMemoryConversationMemory"/> (tests / single-process)
/// and <c>RedisConversationMemory</c> (distributed, in the <c>Ananke.Redis</c> package).
/// </remarks>
public interface IConversationMemory
{
    /// <summary>Appends one or more messages to the conversation history for <paramref name="sessionId"/>.</summary>
    Task AddAsync(string sessionId, IEnumerable<AgentMessage> messages, CancellationToken ct = default);

    /// <summary>Appends a single message to the conversation history for <paramref name="sessionId"/>.</summary>
    Task AddAsync(string sessionId, AgentMessage message, CancellationToken ct = default);

    /// <summary>
    /// Returns the full conversation history for <paramref name="sessionId"/> in chronological order,
    /// or an empty list if no history exists.
    /// </summary>
    Task<IReadOnlyList<AgentMessage>> GetHistoryAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Deletes all conversation history for <paramref name="sessionId"/>.</summary>
    Task ClearAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Removes all sessions whose TTL has expired. No-op if the implementation does not support TTL.</summary>
    Task CleanupExpiredAsync(CancellationToken ct = default);
}
