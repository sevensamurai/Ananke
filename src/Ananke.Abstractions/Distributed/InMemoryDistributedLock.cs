using System.Collections.Concurrent;
using System.Text.Json;

namespace Ananke.Abstractions.Distributed;

/// <summary>
/// Zero-config in-memory implementation of <see cref="IDistributedLock"/> and
/// <see cref="IKeyValueDataAdapter"/>.
/// Replaces Redis for demos and unit tests — no external infrastructure required.
/// Locks each resource id on its own, within this instance, for as long as the action runs.
/// Reads return <c>null</c> only for a missing key; a stored value that is not valid JSON for
/// the requested type throws, as <see cref="IKeyValueDataAdapter"/> requires.
/// </summary>
/// <param name="clock">Time source for value expiry. Defaults to <see cref="TimeProvider.System"/>.</param>
public sealed class InMemoryDistributedLock(TimeProvider? clock = null) : IDistributedLock, IKeyValueDataAdapter
{
    private readonly record struct Entry(string Value, DateTimeOffset? ExpiresAt);

    private readonly ConcurrentDictionary<string, Entry> _store = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, byte> _held = new();
    private const int MinCompactAt = 64;
    private readonly Lock _sweepGate = new();

    /// <summary>
    /// Keys written with an expiry, by that expiry in UTC ticks. A key written again keeps its older
    /// entries until they come due or the queue is rebuilt from the store.
    /// </summary>
    private readonly PriorityQueue<string, long> _expiries = new();

    /// <summary>Queue length past which <see cref="_expiries"/> is rebuilt from the store.</summary>
    private int _compactAt = MinCompactAt;

    /// <summary>
    /// UTC ticks of the earliest expiry in <see cref="_expiries"/>, <see cref="long.MaxValue"/> when it is
    /// empty. Read without the lock, so a write with no expiry and nothing due takes no lock.
    /// </summary>
    private long _nextExpiry = long.MaxValue;

    /// <summary>Entries in the store, expired ones not yet removed included.</summary>
    internal int Count => _store.Count;

    /// <summary>The value under <paramref name="key"/>, or <c>null</c> when it is missing or has expired.</summary>
    private string? Read(string key)
    {
        if (!_store.TryGetValue(key, out var entry))
            return null;
        if (entry.ExpiresAt is { } at && at <= _clock.GetUtcNow())
        {
            _store.TryRemove(KeyValuePair.Create(key, entry));
            return null;
        }
        return entry.Value;
    }

    public Task<string?> GetValueAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(Read(key));

    public Task<T?> GetValueAsync<T>(string key, CancellationToken ct = default)
    {
        if (Read(key) is not { } json)
            return Task.FromResult<T?>(default);
        try
        {
            return Task.FromResult(JsonSerializer.Deserialize<T>(json));
        }
        catch (Exception ex)
        {
            return Task.FromException<T?>(ex);
        }
    }

    public Task SetValueAsync(string key, string value, CancellationToken ct = default) =>
        SetValueAsync(key, value, null, ct);

    public Task SetValueAsync<T>(string key, T value, CancellationToken ct = default) =>
        SetValueAsync(key, JsonSerializer.Serialize(value), null, ct);

    public Task SetValueAsync(string key, string value, TimeSpan? expiry, CancellationToken ct = default)
    {
        if (expiry is { } ttl)
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero, nameof(expiry));
        var now = _clock.GetUtcNow();
        var entry = new Entry(value, now + expiry);
        _store[key] = entry;
        if (entry.ExpiresAt is null && now.UtcTicks < Volatile.Read(ref _nextExpiry))
            return Task.CompletedTask;
        lock (_sweepGate)
        {
            if (entry.ExpiresAt is { } at)
                Track(key, at.UtcTicks);
            RemoveExpired(now);
            Volatile.Write(ref _nextExpiry, _expiries.TryPeek(out _, out var next) ? next : long.MaxValue);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Queues <paramref name="key"/> to be removed at <paramref name="at"/>; once the queue holds more than
    /// <see cref="_compactAt"/> entries, rebuilds it with one entry per key in the store that has an expiry.
    /// </summary>
    private void Track(string key, long at)
    {
        _expiries.Enqueue(key, at);
        if (_expiries.Count <= _compactAt)
            return;
        _expiries.Clear();
        foreach (var (k, e) in _store)
        {
            if (e.ExpiresAt is { } expiresAt)
                _expiries.Enqueue(k, expiresAt.UtcTicks);
        }
        _compactAt = Math.Max(MinCompactAt, 2 * _expiries.Count);
    }

    /// <summary>
    /// Removes the entries expired at <paramref name="now"/>, so keys that are never read again
    /// don't stay in memory. Visits only the queued expiries that are due.
    /// </summary>
    private void RemoveExpired(DateTimeOffset now)
    {
        while (_expiries.TryPeek(out var key, out var at) && at <= now.UtcTicks)
        {
            _expiries.Dequeue();
            if (_store.TryGetValue(key, out var entry) && entry.ExpiresAt is { } expiresAt && expiresAt <= now)
                _store.TryRemove(KeyValuePair.Create(key, entry));
        }
    }

    public Task SetValueAsync<T>(string key, T value, TimeSpan? expiry, CancellationToken ct = default) =>
        SetValueAsync(key, JsonSerializer.Serialize(value), expiry, ct);

    public Task<bool> RemoveAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(Read(key) is not null && _store.TryRemove(key, out _));

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(Read(key) is not null);

    public async Task<CoordinatedActionResult<R>> RunCoordinatedActionAsync<R>(
        string resourceId, Func<Task<R>> action, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!_held.TryAdd(resourceId, 0))
            return CoordinatedActionResult<R>.LockFailed();
        try
        {
            var result = await action().ConfigureAwait(false);
            return CoordinatedActionResult<R>.Succeeded(result);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return CoordinatedActionResult<R>.Failed(ex.Message, ex);
        }
        finally
        {
            _held.TryRemove(resourceId, out _);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
