namespace Ananke.Abstractions.Distributed;

/// <summary>
/// Interface for key-value data storage operations.
/// Implementations handle their own connection lifecycle internally
/// (e.g. via DI constructor with <c>IOptions&lt;CacheConfig&gt;</c> and lazy connection).
/// </summary>
/// <remarks>
/// A read returns <c>null</c> (or <c>default</c>) only when the key does not exist. Every
/// operation throws when the store cannot answer — unreachable, timed out, or holding a value
/// that cannot be deserialized — so a caller never reads a failure as a missing key.
/// </remarks>
public interface IKeyValueDataAdapter : IAsyncDisposable
{
    /// <summary>
    /// Gets a string value by key; <c>null</c> when the key does not exist.
    /// Throws when the store cannot be read.
    /// </summary>
    Task<string?> GetValueAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Gets a typed value by key, deserializing from JSON; <c>default</c> when the key does not exist.
    /// Throws when the store cannot be read or the value cannot be deserialized.
    /// </summary>
    Task<T?> GetValueAsync<T>(string key, CancellationToken ct = default);

    /// <summary>
    /// Sets a string value by key, with no expiry. Throws when the store cannot be written.
    /// </summary>
    Task SetValueAsync(string key, string value, CancellationToken ct = default) =>
        SetValueAsync(key, value, null, ct);

    /// <summary>
    /// Sets a typed value by key, serializing to JSON, with no expiry. Throws when the store cannot be written.
    /// </summary>
    Task SetValueAsync<T>(string key, T value, CancellationToken ct = default) =>
        SetValueAsync(key, value, null, ct);

    /// <summary>
    /// Sets a string value by key. After <paramref name="expiry"/> the key no longer exists; <c>null</c> keeps it
    /// until removed. Setting a key replaces any expiry it had. Throws when the store cannot be written, and
    /// <see cref="ArgumentOutOfRangeException"/> when <paramref name="expiry"/> is zero or negative.
    /// </summary>
    Task SetValueAsync(string key, string value, TimeSpan? expiry, CancellationToken ct = default);

    /// <summary>
    /// Sets a typed value by key, serializing to JSON, with an optional expiry as for
    /// <see cref="SetValueAsync(string, string, TimeSpan?, CancellationToken)"/>.
    /// </summary>
    Task SetValueAsync<T>(string key, T value, TimeSpan? expiry, CancellationToken ct = default);

    /// <summary>
    /// Removes a value by key; <c>false</c> when the key did not exist.
    /// Throws when the store cannot be written.
    /// </summary>
    Task<bool> RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Checks if a key exists. Throws when the store cannot be read.
    /// </summary>
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}
