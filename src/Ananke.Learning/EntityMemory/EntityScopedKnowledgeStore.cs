using Ananke.Orchestration.Knowledge;

namespace Ananke.Learning.EntityMemory;

/// <summary>
/// Decorator that scopes an <see cref="IKnowledgeStore"/> to a specific entity
/// by injecting entity metadata on upsert and adding entity filters on search
/// and delete operations.
/// </summary>
/// <remarks>
/// A caller's filter is copied with its <see cref="KnowledgeFilter.AnyOf"/>, and the entity is added as an entry, which
/// a document must match whatever <see cref="KnowledgeFilter.AnyOf"/> holds, so a filter can't reach another entity.
/// </remarks>
/// <param name="inner">The shared knowledge store.</param>
/// <param name="entityId">The entity to scope to.</param>
#pragma warning disable ANNKEEXP004 // implements the experimental listing
public sealed class EntityScopedKnowledgeStore(
    IKnowledgeStore inner, string entityId) : IListableKnowledgeStore
#pragma warning restore ANNKEEXP004
{
    internal const string EntityIdMetadataKey = "entity_id";

    /// <inheritdoc />
    public Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(
        string query, SearchOptions? options = null, CancellationToken ct = default)
    {
        options ??= new SearchOptions();
        var filter = (options.Filter ?? new KnowledgeFilter()).Copy();
        filter[EntityIdMetadataKey] = entityId;

        return inner.SearchAsync(query, options with { Filter = filter }, ct);
    }

    /// <inheritdoc />
    public Task UpsertAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken ct = default)
    {
        var scoped = documents.Select(doc =>
        {
            var metadata = new Dictionary<string, string>(doc.Metadata)
            {
                [EntityIdMetadataKey] = entityId
            };
            return doc with { Metadata = metadata };
        });

        return inner.UpsertAsync(scoped, ct);
    }

    /// <inheritdoc />
    public Task DeleteAsync(KnowledgeFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
#pragma warning disable ANNKEEXP004 // reads the experimental AnyOf
        if (filter.Count == 0 && filter.AnyOf.Count == 0)
            throw new ArgumentException("A delete with an empty filter would remove every document; name what to delete.", nameof(filter));
#pragma warning restore ANNKEEXP004

        var scopedFilter = (filter ?? new KnowledgeFilter()).Copy();
        scopedFilter[EntityIdMetadataKey] = entityId;

        return inner.DeleteAsync(scopedFilter, ct);
    }

    /// <inheritdoc />
#pragma warning disable ANNKEEXP004 // lists through the experimental listing
    public Task<IReadOnlyList<KnowledgeChunk>> ListAsync(
        KnowledgeFilter filter, int limit = 1000, KnowledgeDateOrder? order = null, CancellationToken ct = default)
    {
        if (inner is not IListableKnowledgeStore listable)
            throw new NotSupportedException($"The store this one scopes ({inner.GetType().Name}) can't list.");

        var scopedFilter = (filter ?? new KnowledgeFilter()).Copy();
        scopedFilter[EntityIdMetadataKey] = entityId;
        return listable.ListAsync(scopedFilter, limit, order, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The entity is set in both the listed metadata and the new one, as on upsert, so a chunk of another entity is
    /// never changed and a chunk never leaves this entity.
    /// </remarks>
    public Task UpdateMetadataAsync(KnowledgeChunk listed, IReadOnlyDictionary<string, string> metadata, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(listed);
        ArgumentNullException.ThrowIfNull(metadata);
        if (inner is not IListableKnowledgeStore listable)
            throw new NotSupportedException($"The store this one scopes ({inner.GetType().Name}) can't list.");

        var scoped = listed with { Metadata = new Dictionary<string, string>(listed.Metadata) { [EntityIdMetadataKey] = entityId } };
        return listable.UpdateMetadataAsync(scoped, new Dictionary<string, string>(metadata) { [EntityIdMetadataKey] = entityId }, ct);
    }
#pragma warning restore ANNKEEXP004
}
