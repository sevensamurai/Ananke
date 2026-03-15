using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Ananke.AspNetCore.Sessions;

/// <summary>
/// Thread-safe in-memory session store backed by <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// Tracks active sessions keyed by string ID. Sessions persist across HTTP requests
/// and are removed explicitly (e.g. when a workflow completes).
/// <para>
/// <see cref="GetOrCreate"/> uses
/// <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey,Func{TKey,TValue})"/>
/// so concurrent requests for the same session ID are safe — the factory runs at most once.
/// </para>
/// </summary>
/// <typeparam name="T">The session type.</typeparam>
public sealed class InMemorySessionStore<T> where T : class
{
    private readonly ConcurrentDictionary<string, T> _sessions = new();

    /// <summary>Gets the number of active sessions.</summary>
    public int Count => _sessions.Count;

    /// <summary>
    /// Returns the existing session or atomically creates one via <paramref name="factory"/>.
    /// </summary>
    public T GetOrCreate(string sessionId, Func<T> factory) =>
        _sessions.GetOrAdd(sessionId, _ => factory());

    /// <summary>
    /// Attempts to retrieve an existing session.
    /// Returns <see langword="null"/> if the session does not exist.
    /// </summary>
    public T? Get(string sessionId) =>
        _sessions.TryGetValue(sessionId, out var session) ? session : null;

    /// <summary>
    /// Attempts to retrieve an existing session.
    /// Returns <see langword="true"/> if the session exists.
    /// </summary>
    public bool TryGet(string sessionId, [NotNullWhen(true)] out T? session) =>
        _sessions.TryGetValue(sessionId, out session);

    /// <summary>
    /// Removes a session by ID. No-op if the session does not exist.
    /// </summary>
    public bool Remove(string sessionId) =>
        _sessions.TryRemove(sessionId, out _);

    /// <summary>
    /// Removes a session by ID and returns it.
    /// Returns <see langword="null"/> if the session does not exist.
    /// </summary>
    public T? RemoveAndGet(string sessionId) =>
        _sessions.TryRemove(sessionId, out var session) ? session : null;
}
