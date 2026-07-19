using System.Collections.Concurrent;
using System.Text.Json;

namespace Ananke.Orchestration.Checkpointing;

public sealed class InMemoryCheckpointStore : ICheckpointStore
{
    private readonly record struct Entry(string Json, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, Entry> _store = new();
    private readonly TimeProvider _timeProvider;

    /// <param name="timeProvider">
    /// Clock used to evaluate checkpoint expiry. Defaults to <see cref="TimeProvider.System"/>;
    /// inject a fake in tests to assert TTL behavior without sleeping.
    /// </param>
    public InMemoryCheckpointStore(TimeProvider? timeProvider = null) =>
        _timeProvider = timeProvider ?? TimeProvider.System;

    public Task SaveAsync<TState>(Checkpoint<TState> checkpoint, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(checkpoint);
        _store[checkpoint.ExecutionId] = new Entry(json, checkpoint.ExpiresAt);
        return Task.CompletedTask;
    }

    public Task<Checkpoint<TState>?> LoadAsync<TState>(string executionId, CancellationToken ct = default)
    {
        if (!_store.TryGetValue(executionId, out var entry))
            return Task.FromResult<Checkpoint<TState>?>(null);

        if (entry.ExpiresAt <= _timeProvider.GetUtcNow())
        {
            _store.TryRemove(executionId, out _);
            return Task.FromResult<Checkpoint<TState>?>(null);
        }

        var checkpoint = JsonSerializer.Deserialize<Checkpoint<TState>>(entry.Json);
        return Task.FromResult(checkpoint);
    }

    public Task DeleteAsync(string executionId, CancellationToken ct = default)
    {
        _store.TryRemove(executionId, out _);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string executionId, CancellationToken ct = default)
    {
        if (!_store.TryGetValue(executionId, out var entry))
            return Task.FromResult(false);

        if (entry.ExpiresAt <= _timeProvider.GetUtcNow())
        {
            _store.TryRemove(executionId, out _);
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    public Task CleanupExpiredAsync(CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var key in _store.Keys.ToList())
        {
            if (_store.TryGetValue(key, out var entry) && entry.ExpiresAt <= now)
                _store.TryRemove(key, out _);
        }
        return Task.CompletedTask;
    }

    public void Clear() => _store.Clear();

    public int Count => _store.Count;
}
