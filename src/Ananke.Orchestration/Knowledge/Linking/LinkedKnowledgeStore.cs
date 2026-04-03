namespace Ananke.Orchestration.Knowledge.Linking;

/// <summary>
/// Decorator over <see cref="IKnowledgeStore"/> that expands search results through
/// a <see cref="IDocumentLinkGraph"/>. For each top vector-search result, linked chunks
/// are retrieved via graph traversal and merged into the final result set.
/// </summary>
/// <remarks>
/// <para>
/// This implements the "dual retrieval" pattern from ADR-012: vector similarity finds
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
/// </remarks>
public sealed class LinkedKnowledgeStore : IKnowledgeStore
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

        // Expand from top seeds via graph traversal
        var seeds = results.Take(_linkOptions.ExpansionSeeds).ToList();
        foreach (var seed in seeds)
        {
            var links = await _graph.GetLinksAsync(seed.Id, _linkOptions.MaxHops, ct);

            foreach (var link in links)
            {
                if (merged.ContainsKey(link.TargetId))
                    continue;

                // Retrieve the linked chunk from the inner store by ID filter
                var linked = await _inner.SearchAsync(
                    query,
                    new SearchOptions
                    {
                        TopK = 1,
                        Filter = new KnowledgeFilter { ["id"] = link.TargetId }
                    },
                    ct);

                if (linked.Count == 0)
                {
                    // Fallback: search with a minimal query scoped to the ID.
                    // Some stores don't support id-based filtering; skip gracefully.
                    continue;
                }

                var graphScore = seed.Score * link.Weight * _linkOptions.GraphScoreDiscount;
                merged.TryAdd(link.TargetId, linked[0] with { Score = graphScore });
            }
        }

        // Re-rank by score and apply TopK
        return merged.Values
            .OrderByDescending(c => c.Score)
            .Take(topK)
            .ToList();
    }

    /// <inheritdoc />
    public Task UpsertAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken ct = default) =>
        _inner.UpsertAsync(documents, ct);

    /// <inheritdoc />
    public async Task DeleteAsync(KnowledgeFilter filter, CancellationToken ct = default)
    {
        // Best-effort link cleanup: when the filter targets a specific chunk ID,
        // remove its links before deleting from the inner store.
        // Source-level link cleanup is not attempted here because the IKnowledgeStore
        // contract does not support listing chunks by filter without a query.
        // Callers who need full graph cleanup on source deletion should remove links
        // explicitly via IDocumentLinkGraph.RemoveLinksAsync for each known chunk ID.
        if (filter.TryGetValue("id", out var id))
            await _graph.RemoveLinksAsync(id, ct);

        await _inner.DeleteAsync(filter, ct);
    }
}
