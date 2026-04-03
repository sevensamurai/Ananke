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
    Task<string?> GetValueAsync(string key);

    /// <summary>
    /// Gets a typed value by key, deserializing from JSON
    /// </summary>
    Task<T?> GetValueAsync<T>(string key);

    /// <summary>
    /// Sets a string value by key
    /// </summary>
    Task SetValueAsync(string key, string value);

    /// <summary>
    /// Sets a typed value by key, serializing to JSON
    /// </summary>
    Task SetValueAsync<T>(string key, T value);

    /// <summary>
    /// Removes a value by key
    /// </summary>
    Task<bool> RemoveAsync(string key);

    /// <summary>
    /// Checks if a key exists
    /// </summary>
    Task<bool> ExistsAsync(string key);
}
