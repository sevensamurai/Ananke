using Ananke.Orchestration.Knowledge.Catalog;

namespace Ananke.Orchestration.Knowledge.Linking;

/// <summary>
/// Decorator over <see cref="IKnowledgeStore"/> that expands search results through
/// a <see cref="IDocumentLinkGraph"/>. For each top vector-search result, linked chunks
/// are retrieved via graph traversal and merged into the final result set.
/// </summary>
/// <remarks>
/// <para>
/// This implements the "dual retrieval" pattern: vector similarity finds
/// chunks that are <em>about</em> similar topics, while graph traversal finds chunks
/// that are <em>structurally connected</em> via relationships established during
/// post-ingestion analysis. The two modalities have complementary failure modes.
/// </para>
/// <para>
/// Composes with <see cref="Catalog.CatalogAwareKnowledgeStore"/> and any other
/// <see cref="IKnowledgeStore"/> decorator:
/// </para>
/// <code>
/// var inner = new InMemoryKnowledgeStore(embedder);
/// var catalogAware = new CatalogAwareKnowledgeStore(inner, catalog, extractor);
/// var linked = new LinkedKnowledgeStore(catalogAware, linkGraph);
/// </code>
/// <para>
/// On <see cref="DeleteAsync"/>, a chunk's links are removed before the chunk. A filter naming only ids (<c>id</c>)
/// removes their links, whether the chunks are still stored or not. Any other filter, over an inner store that is an
/// <see cref="IListableKnowledgeStore"/>, removes the links of the chunks it lists for that filter; over one that is
/// not, or wraps one that can't list, only those of the ids the filter names, whether or not another key keeps the
/// chunk.
/// </para>
/// </remarks>
#pragma warning disable ANNKEEXP004 // implements the experimental listing
public sealed class LinkedKnowledgeStore : IListableKnowledgeStore
#pragma warning restore ANNKEEXP004
{
    private readonly IKnowledgeStore _inner;
    private readonly IDocumentLinkGraph _graph;
    private readonly LinkedSearchOptions _linkOptions;

    /// <summary>
    /// Creates a linked knowledge store decorator.
    /// </summary>
    /// <param name="inner">The underlying knowledge store for chunk storage and vector search.</param>
    /// <param name="graph">The document link graph for cross-chunk traversal.</param>
    /// <param name="linkOptions">
    /// Options controlling graph expansion behavior. When <see langword="null"/>,
    /// defaults are used (expand enabled, 3 seeds, 1 hop, 0.8 discount).
    /// </param>
    public LinkedKnowledgeStore(
        IKnowledgeStore inner,
        IDocumentLinkGraph graph,
        LinkedSearchOptions? linkOptions = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(graph);

        _inner = inner;
        _graph = graph;
        _linkOptions = linkOptions ?? new LinkedSearchOptions();
    }

    /// <summary>The store this one decorates.</summary>
    internal IKnowledgeStore Inner => _inner;

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(
        string query, SearchOptions? options = null, CancellationToken ct = default)
    {
        var results = await _inner.SearchAsync(query, options, ct);

        if (!_linkOptions.ExpandGraph || results.Count == 0)
            return results;

        var topK = options?.TopK ?? 5;

        // Collect vector results into a mutable set keyed by ID
        var merged = new Dictionary<string, KnowledgeChunk>(StringComparer.Ordinal);
        foreach (var chunk in results)
            merged.TryAdd(chunk.Id, chunk);

        // A linked chunk the caller's filter would not match is not added; its id is checked here and every other key
        // by the inner store.
        var allowedIds = options?.Filter?.ValuesOf("id");

        // A seed's linked chunks are read by id, within the caller's filter, through one listing of the inner store,
        // which embeds nothing; over a store that can't list, each by a search of one with the caller's options and no
        // score threshold, since a linked chunk unlike the query (a negative cosine) is still linked.
        var canList = true;
        SearchOptions? lookup = null;

        // Expand from top seeds via graph traversal
        var seeds = results.Take(_linkOptions.ExpansionSeeds).ToList();
        foreach (var seed in seeds)
        {
            var links = await _graph.GetLinksAsync(seed.Id, _linkOptions.MaxHops, ct);
            var targets = links.Select(l => l.TargetId)
                .Where(id => !merged.ContainsKey(id) && (allowedIds is null || allowedIds.Contains(id, StringComparer.Ordinal)))
                .ToHashSet(StringComparer.Ordinal);
            if (targets.Count == 0)
                continue;

            // Through a catalog-aware store, marked from its catalog as its search marks results.
            var byIds = ById([.. targets], options?.Filter);
            var listed = !canList ? null
                : _inner is CatalogAwareKnowledgeStore catalogAware
                    ? await catalogAware.TryListMarkedAsync(byIds, targets.Count, ct).ConfigureAwait(false)
                    : await _inner.TryListAsync(byIds, targets.Count, ct).ConfigureAwait(false);
            canList = listed is not null;
            var found = listed?.DistinctBy(c => c.Id, StringComparer.Ordinal).ToDictionary(c => c.Id, StringComparer.Ordinal);

            foreach (var link in links)
            {
                if (merged.ContainsKey(link.TargetId) || !targets.Contains(link.TargetId))
                    continue;

                KnowledgeChunk? chunk;
                if (found is not null)
                {
                    chunk = found.GetValueOrDefault(link.TargetId);
                }
                else
                {
                    lookup ??= (options ?? new SearchOptions()) with
                    {
                        TopK = 1,
                        ScoreThreshold = float.MinValue,
                        MaxPerSource = null,
                        JoinPerSource = false,
                    };
                    var searched = await _inner.SearchAsync(
                        query, lookup with { Filter = ById([link.TargetId], options?.Filter) }, ct).ConfigureAwait(false);
                    chunk = searched.Count > 0 ? searched[0] : null;
                }

                // Not found within the filter, or the store can't filter by id: the link is not followed.
                if (chunk is null)
                    continue;

                var graphScore = seed.Score * link.Weight * _linkOptions.GraphScoreDiscount;
                merged.TryAdd(link.TargetId, chunk with { Score = graphScore });
            }
        }

        // Re-rank by score and apply TopK
        return merged.Values
            .OrderByDescending(c => c.Score)
            .Take(topK)
            .ToList();
    }

    /// <summary>
    /// The caller's filter, entries and <see cref="KnowledgeFilter.AnyOf"/>, with its id one of <paramref name="ids"/>:
    /// an entry for one id, <see cref="KnowledgeFilter.AnyOf"/> for several.
    /// </summary>
    private static KnowledgeFilter ById(IReadOnlyList<string> ids, KnowledgeFilter? filter)
    {
        var byId = filter?.Copy() ?? new KnowledgeFilter();
#pragma warning disable ANNKEEXP004 // writes AnyOf, experimental outside this assembly
        byId.Remove("id");
        byId.AnyOf.Remove("id");
        if (ids.Count == 1)
            byId["id"] = ids[0];
        else
            byId.AnyOf["id"] = ids;
#pragma warning restore ANNKEEXP004
        return byId;
    }

    /// <inheritdoc />
    public Task UpsertAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken ct = default) =>
        _inner.UpsertAsync(documents, ct);

    /// <inheritdoc />
    public async Task DeleteAsync(KnowledgeFilter filter, CancellationToken ct = default)
    {
        // Refused before the listing below, which an empty filter would make read the whole store.
        KnowledgeFilter.ThrowIfEmptyForDelete(filter);

        // Links go first, so a failure leaves the chunks in place and a retry finds them again. A filter naming only ids
        // unlinks them, stored or not; another filter unlinks the chunks the inner store lists for it, when it can list.
        var namedIds = filter.ValuesOf("id");
#pragma warning disable ANNKEEXP004 // reads AnyOf, experimental outside this assembly
        var onlyIds = namedIds is not null && filter.Keys.Concat(filter.AnyOf.Keys).All(k => k == "id");
#pragma warning restore ANNKEEXP004
        var ids = !onlyIds && await _inner.TryListAsync(filter, int.MaxValue, ct).ConfigureAwait(false) is { } listed
            ? listed.Select(c => c.Id)
            : namedIds ?? [];

        foreach (var id in ids)
            await _graph.RemoveLinksAsync(id, ct).ConfigureAwait(false);

        await _inner.DeleteAsync(filter, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
#pragma warning disable ANNKEEXP004 // lists through the experimental listing
    public Task<IReadOnlyList<KnowledgeChunk>> ListAsync(
        KnowledgeFilter filter, int limit = 1000, KnowledgeDateOrder? order = null, CancellationToken ct = default) =>
        _inner.ListThroughAsync(filter, limit, order, ct);

    /// <inheritdoc />
    /// <remarks>Through the inner store; links are left as they are.</remarks>
    public Task UpdateMetadataAsync(KnowledgeChunk listed, IReadOnlyDictionary<string, string> metadata, CancellationToken ct = default) =>
        _inner.UpdateMetadataThroughAsync(listed, metadata, ct);
#pragma warning restore ANNKEEXP004
}
