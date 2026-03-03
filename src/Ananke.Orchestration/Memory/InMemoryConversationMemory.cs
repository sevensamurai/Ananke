using System.Collections.Concurrent;
using Ananke.Orchestration.Agents;

namespace Ananke.Orchestration.Memory;

/// <summary>
/// In-memory <see cref="IConversationMemory"/> for testing and single-process scenarios.
/// Supports optional TTL-based expiry aligned with checkpoint TTLs.
/// </summary>
public sealed class InMemoryConversationMemory : IConversationMemory
{
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly TimeSpan? _ttl;

    /// <summary>
    /// Creates a new in-memory conversation memory store.
    /// </summary>
    /// <param name="ttl">
    /// Optional time-to-live for sessions. When set, sessions that have not been
    /// written to within this duration are eligible for cleanup via
    /// <see cref="CleanupExpiredAsync"/>. When <see langword="null"/>, sessions never expire.
    /// </param>
    public InMemoryConversationMemory(TimeSpan? ttl = null)
    {
        _ttl = ttl;
    }

    /// <inheritdoc />
    public Task AddAsync(string sessionId, IEnumerable<AgentMessage> messages, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(messages);

        var session = _sessions.GetOrAdd(sessionId, _ => new Session());
        lock (session.Lock)
        {
            session.Messages.AddRange(messages);
            session.LastWriteUtc = DateTimeOffset.UtcNow;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AddAsync(string sessionId, AgentMessage message, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(message);

        var session = _sessions.GetOrAdd(sessionId, _ => new Session());
        lock (session.Lock)
        {
            session.Messages.Add(message);
            session.LastWriteUtc = DateTimeOffset.UtcNow;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentMessage>> GetHistoryAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (!_sessions.TryGetValue(sessionId, out var session))
            return Task.FromResult<IReadOnlyList<AgentMessage>>([]);

        if (IsExpired(session))
        {
            _sessions.TryRemove(sessionId, out _);
            return Task.FromResult<IReadOnlyList<AgentMessage>>([]);
        }

        lock (session.Lock)
        {
            return Task.FromResult<IReadOnlyList<AgentMessage>>([.. session.Messages]);
        }
    }

    /// <inheritdoc />
    public Task ClearAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        _sessions.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CleanupExpiredAsync(CancellationToken ct = default)
    {
        if (_ttl is null) return Task.CompletedTask;

        foreach (var key in _sessions.Keys)
        {
            if (_sessions.TryGetValue(key, out var session) && IsExpired(session))
                _sessions.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    /// <summary>Returns the number of active (non-expired) sessions.</summary>
    public int SessionCount => _sessions.Count;

    private bool IsExpired(Session session) =>
        _ttl.HasValue && session.LastWriteUtc + _ttl.Value < DateTimeOffset.UtcNow;

    private sealed class Session
    {
        public readonly object Lock = new();
        public readonly List<AgentMessage> Messages = [];
        public DateTimeOffset LastWriteUtc = DateTimeOffset.UtcNow;
    }
}
