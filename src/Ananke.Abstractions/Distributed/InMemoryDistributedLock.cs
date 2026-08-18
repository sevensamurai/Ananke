using System.Collections.Concurrent;
using System.Text.Json;

namespace Ananke.Abstractions.Distributed;

/// <summary>
/// Zero-config in-memory implementation of <see cref="IDistributedLock"/> and
/// <see cref="IKeyValueDataAdapter"/>.
/// Replaces Redis for demos and unit tests — no external infrastructure required.
/// </summary>
public sealed class InMemoryDistributedLock : IDistributedLock, IKeyValueDataAdapter
{
    private readonly ConcurrentDictionary<string, string> _store = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public Task<string?> GetValueAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_store.GetValueOrDefault(key));

    public Task<T?> GetValueAsync<T>(string key, CancellationToken ct = default)
    {
        if (!_store.TryGetValue(key, out var json))
            return Task.FromResult<T?>(default);
        return Task.FromResult(JsonSerializer.Deserialize<T>(json));
    }

    public Task SetValueAsync(string key, string value, CancellationToken ct = default)
    {
        _store[key] = value;
        return Task.CompletedTask;
    }

    public Task SetValueAsync<T>(string key, T value, CancellationToken ct = default)
    {
        _store[key] = JsonSerializer.Serialize(value);
        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_store.TryRemove(key, out _));

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(_store.ContainsKey(key));

    public async Task<CoordinatedActionResult<R>> RunCoordinatedActionAsync<R>(
        string resourceId, Func<Task<R>> action, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            var result = await action();
            return CoordinatedActionResult<R>.Succeeded(result);
        }
        catch (Exception ex)
        {
            return CoordinatedActionResult<R>.Failed(ex.Message, ex);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<CoordinatedActionResult<R>> RunCoordinatedActionWithRetryAsync<R>(
        string resourceId, Func<Task<R>> action, int maxRetries = 3, int retryDelayMs = 100, CancellationToken ct = default)
    {
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            var result = await RunCoordinatedActionAsync(resourceId, action, ct);
            if (result.Success) return result;
            if (attempt < maxRetries) await Task.Delay(retryDelayMs, ct);
        }
        return CoordinatedActionResult<R>.Failed("All retry attempts exhausted");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
