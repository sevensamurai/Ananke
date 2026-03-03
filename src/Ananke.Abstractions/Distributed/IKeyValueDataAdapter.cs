using Ananke.Abstractions.Config;

namespace Ananke.Abstractions.Distributed;

/// <summary>
/// Interface for key-value data storage operations
/// </summary>
public interface IKeyValueDataAdapter : IAsyncDisposable
{
    /// <summary>
    /// Sets up the connection to the data store
    /// </summary>
    Task SetupAsync(CacheConfig config, CancellationToken token = default);

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
