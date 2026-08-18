namespace Ananke.Abstractions.Distributed;

/// <summary>
/// Interface for key-value data storage operations.
/// Implementations handle their own connection lifecycle internally
/// (e.g. via DI constructor with <c>IOptions&lt;CacheConfig&gt;</c> and lazy connection).
/// </summary>
public interface IKeyValueDataAdapter : IAsyncDisposable
{
    /// <summary>
    /// Gets a string value by key
    /// </summary>
    Task<string?> GetValueAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Gets a typed value by key, deserializing from JSON
    /// </summary>
    Task<T?> GetValueAsync<T>(string key, CancellationToken ct = default);

    /// <summary>
    /// Sets a string value by key
    /// </summary>
    Task SetValueAsync(string key, string value, CancellationToken ct = default);

    /// <summary>
    /// Sets a typed value by key, serializing to JSON
    /// </summary>
    Task SetValueAsync<T>(string key, T value, CancellationToken ct = default);

    /// <summary>
    /// Removes a value by key
    /// </summary>
    Task<bool> RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Checks if a key exists
    /// </summary>
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}
