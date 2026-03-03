using System.Text.Json;
using Ananke.Abstractions.Config;

namespace Ananke.Abstractions.Distributed;

/// <summary>
/// Zero-config in-memory implementation of <see cref="IDistributedLock"/>.
/// Replaces Redis for demos and unit tests — no external infrastructure required.
/// </summary>
public sealed class InMemoryDistributedLock : IDistributedLock
{
    private readonly Dictionary<string, string> _store = [];
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public Task SetupAsync(CacheConfig config, CancellationToken token = default) =>
        Task.CompletedTask;

    public Task<string?> GetValueAsync(string key)
    {
        _store.TryGetValue(key, out var value);
        return Task.FromResult(value);
    }

    public Task<T?> GetValueAsync<T>(string key)
    {
        if (!_store.TryGetValue(key, out var json))
            return Task.FromResult<T?>(default);
        return Task.FromResult(JsonSerializer.Deserialize<T>(json));
    }

    public Task SetValueAsync(string key, string value)
    {
        _store[key] = value;
        return Task.CompletedTask;
    }

    public Task SetValueAsync<T>(string key, T value)
    {
        _store[key] = JsonSerializer.Serialize(value);
        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(string key) =>
        Task.FromResult(_store.Remove(key));

    public Task<bool> ExistsAsync(string key) =>
        Task.FromResult(_store.ContainsKey(key));

    public async Task<CoordinatedActionResult<R>> RunCoordinatedActionAsync<R>(
        string resourceId, Func<Task<R>> action)
    {
        await _semaphore.WaitAsync();
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
        string resourceId, Func<Task<R>> action, int maxRetries = 3, int retryDelayMs = 100)
    {
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            var result = await RunCoordinatedActionAsync(resourceId, action);
            if (result.Success) return result;
            if (attempt < maxRetries) await Task.Delay(retryDelayMs);
        }
        return CoordinatedActionResult<R>.Failed("All retry attempts exhausted");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
