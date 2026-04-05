namespace Ananke.Learning.EntityMemory;

/// <summary>
/// Creates or retrieves <see cref="IEntityMemory"/> instances on demand.
/// Handles lazy activation and optional idle eviction of in-memory caches.
/// </summary>
/// <remarks>
/// <para>
/// This is the minimal "virtual actor lifecycle" — lazy activation + idle eviction —
/// without an actor framework. The default implementation
/// (<see cref="EntityMemoryProvider"/>) uses a <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}"/>
/// for thread-safe caching of entity facades.
/// </para>
/// <para>
/// Entity scoping is metadata-based, not physical partitioning. All entities share the
/// same underlying stores. <see cref="EvictAsync"/> releases only the cached facade
/// instance — the entity's persisted data in the stores is not deleted.
/// </para>
/// </remarks>
public interface IEntityMemoryProvider
{
    /// <summary>
    /// Gets or creates the memory facade for the given entity.
    /// Thread-safe; concurrent calls for the same entity return the same instance.
    /// </summary>
    /// <param name="entityId">
    /// Opaque identifier for the entity (user ID, customer ID, device serial, etc.).
    /// Must be stable across the entity's lifetime.
    /// </param>
    IEntityMemory GetOrCreate(string entityId);

    /// <summary>
    /// Evicts the cached facade for an idle entity. The underlying persisted
    /// state in the stores is not deleted — only the in-memory facade reference
    /// is released. A subsequent <see cref="GetOrCreate"/> call will create a
    /// fresh facade that reconnects to the same persisted data.
    /// </summary>
    Task EvictAsync(string entityId, CancellationToken ct = default);
}
