using Ananke.Orchestration.Knowledge;

namespace Ananke.Learning.EntityMemory;

/// <summary>
/// Decorator that scopes an <see cref="IKnowledgeStore"/> to a specific entity
/// by injecting entity metadata on upsert and adding entity filters on search
/// and delete operations.
/// </summary>
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
        var filter = CopyFilter(options.Filter);
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
        var scopedFilter = CopyFilter(filter);
        scopedFilter[EntityIdMetadataKey] = entityId;

        return inner.DeleteAsync(scopedFilter, ct);
    }

    /// <inheritdoc />
#pragma warning disable ANNKEEXP004 // lists through the experimental listing
    public Task<IReadOnlyList<KnowledgeChunk>> ListAsync(KnowledgeFilter filter, int limit = 1000, CancellationToken ct = default)
    {
        if (inner is not IListableKnowledgeStore listable)
            throw new NotSupportedException($"The store this one scopes ({inner.GetType().Name}) can't list.");

        var scopedFilter = CopyFilter(filter);
        scopedFilter[EntityIdMetadataKey] = entityId;
        return listable.ListAsync(scopedFilter, limit, ct);
    }
#pragma warning restore ANNKEEXP004

    private static KnowledgeFilter CopyFilter(KnowledgeFilter? source)
    {
        var filter = new KnowledgeFilter();
        if (source is not null)
        {
            foreach (var (key, value) in source)
                filter[key] = value;
        }
        return filter;
    }
}
