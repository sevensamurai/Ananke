using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Memory;

namespace Ananke.Learning.EntityMemory;

/// <summary>
/// Decorator that scopes an <see cref="IConversationMemory"/> to a specific entity
/// by prefixing all session IDs with the entity identifier. This ensures conversation
/// history for different entities is isolated even when sharing the same underlying store.
/// </summary>
/// <param name="inner">The shared conversation memory store.</param>
/// <param name="entityId">The entity to scope to.</param>
public sealed class EntityScopedConversationMemory(
    IConversationMemory inner, string entityId) : IConversationMemory
{
    /// <inheritdoc />
    public Task AddAsync(string sessionId, IEnumerable<AgentMessage> messages, CancellationToken ct = default) =>
        inner.AddAsync(ScopedKey(sessionId), messages, ct);

    /// <inheritdoc />
    public Task AddAsync(string sessionId, AgentMessage message, CancellationToken ct = default) =>
        inner.AddAsync(ScopedKey(sessionId), message, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentMessage>> GetHistoryAsync(string sessionId, CancellationToken ct = default) =>
        inner.GetHistoryAsync(ScopedKey(sessionId), ct);

    /// <inheritdoc />
    public Task ClearAsync(string sessionId, CancellationToken ct = default) =>
        inner.ClearAsync(ScopedKey(sessionId), ct);

    /// <inheritdoc />
    public Task CleanupExpiredAsync(CancellationToken ct = default) =>
        inner.CleanupExpiredAsync(ct);

    private string ScopedKey(string sessionId) => $"{entityId}:{sessionId}";
}
