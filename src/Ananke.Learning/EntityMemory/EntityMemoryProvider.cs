using System.Collections.Concurrent;
using Ananke.Abstractions.Memory;
using Ananke.Learning.Episodes;
using Ananke.Orchestration.Knowledge;


using Ananke.Learning.EmpiricalMemory;

namespace Ananke.Learning.EntityMemory;

/// <summary>
/// Default <see cref="IEntityMemoryProvider"/> that creates entity-scoped memory
/// facades on demand using decorator wrappers around shared infrastructure.
/// </summary>
/// <remarks>
/// <para>
/// All entities share the same underlying stores (single Qdrant collection,
/// single Redis instance, etc.). Entity isolation is achieved via metadata
/// filtering and key prefixing in the decorators, not physical partitioning.
/// </para>
/// <para>
/// Facade instances are cached in a <see cref="ConcurrentDictionary{TKey, TValue}"/>
/// for thread-safe reuse. <see cref="EvictAsync"/> removes the cached instance
/// without deleting persisted data — a subsequent <see cref="GetOrCreate"/>
/// reconnects to the same data.
/// </para>
/// </remarks>
public sealed class EntityMemoryProvider : IEntityMemoryProvider
{
    private readonly ConcurrentDictionary<string, IEntityMemory> _cache = new();
    private readonly IConversationMemory _conversations;
    private readonly IEmpiricalMemory _empirical;
    private readonly IKnowledgeStore _knowledge;
    private readonly IEpisodeStore _episodes;

    /// <summary>
    /// Creates a new entity memory provider backed by shared infrastructure.
    /// </summary>
    /// <param name="conversations">Shared conversation memory store.</param>
    /// <param name="empirical">Shared empirical memory store.</param>
    /// <param name="knowledge">Shared knowledge store.</param>
    /// <param name="episodes">Shared episode store.</param>
    public EntityMemoryProvider(
        IConversationMemory conversations,
        IEmpiricalMemory empirical,
        IKnowledgeStore knowledge,
        IEpisodeStore episodes)
    {
        ArgumentNullException.ThrowIfNull(conversations);
        ArgumentNullException.ThrowIfNull(empirical);
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(episodes);

        _conversations = conversations;
        _empirical = empirical;
        _knowledge = knowledge;
        _episodes = episodes;
    }

    /// <inheritdoc />
    public IEntityMemory GetOrCreate(string entityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        return _cache.GetOrAdd(entityId, CreateFacade);
    }

    /// <inheritdoc />
    public Task EvictAsync(string entityId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        _cache.TryRemove(entityId, out _);
        return Task.CompletedTask;
    }

    private IEntityMemory CreateFacade(string entityId) => new EntityMemoryFacade(
        entityId,
        new EntityScopedConversationMemory(_conversations, entityId),
        new EntityScopedEmpiricalMemory(_empirical, entityId),
        new EntityScopedKnowledgeStore(_knowledge, entityId),
        new EntityScopedEpisodeStore(_episodes, entityId));
}

/// <summary>
/// Default <see cref="IEntityMemory"/> implementation returned by
/// <see cref="EntityMemoryProvider"/>. Composes entity-scoped decorators
/// around shared stores.
/// </summary>
internal sealed record EntityMemoryFacade(
    string EntityId,
    IConversationMemory Conversations,
    IEmpiricalMemory Empirical,
    IKnowledgeStore Knowledge,
    IEpisodeStore Episodes) : IEntityMemory;
