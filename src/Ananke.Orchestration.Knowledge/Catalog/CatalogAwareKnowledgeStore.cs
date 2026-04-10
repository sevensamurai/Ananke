namespace Ananke.Orchestration.Knowledge.Catalog;

/// <summary>
/// Decorator over <see cref="IKnowledgeStore"/> that automatically maintains an
/// <see cref="IKnowledgeCatalog"/> as documents are upserted and deleted.
/// </summary>
/// <remarks>
/// <para>
/// On <see cref="UpsertAsync"/>: documents are grouped by their <c>source</c> metadata key.
/// For each source group, representative text is sent to an optional
/// <see cref="CatalogKeywordExtractor"/> to produce keywords, a category, and a summary.
/// A <see cref="CatalogEntry"/> is then upserted into the catalog with the current timestamp.
/// </para>
/// <para>
/// On <see cref="DeleteAsync"/>: if the filter targets a specific <c>source</c>, the
/// corresponding catalog entry is removed.
/// </para>
/// <para>
/// On <see cref="SearchAsync"/>: results from the inner store are reranked using time-decay
/// when <see cref="TimeDecayOptions"/> are configured. Each result's score is multiplied by
/// a weight derived from its source document's <see cref="CatalogEntry.IndexedAt"/> timestamp.
/// </para>
/// </remarks>
public sealed class CatalogAwareKnowledgeStore : IKnowledgeStore
{
    private readonly IKnowledgeStore _inner;
    private readonly IKnowledgeCatalog _catalog;
    private readonly CatalogKeywordExtractor? _extractor;
    private readonly TimeDecayOptions? _decayOptions;

    /// <summary>
    /// Creates a catalog-aware knowledge store decorator.
    /// </summary>
    /// <param name="inner">The underlying knowledge store for chunk storage and vector search.</param>
    /// <param name="catalog">The catalog to maintain alongside the store.</param>
    /// <param name="extractor">
    /// Optional LLM-based keyword extractor. When provided, upserted documents are enriched
    /// with keywords, a category, and a summary. When <see langword="null"/>, catalog entries
    /// contain only source, timestamp, and chunk count.
    /// </param>
    /// <param name="decayOptions">
    /// Optional time-decay configuration. When provided, search results are reranked so that
    /// newer documents score higher than older ones at similar relevance.
    /// </param>
    public CatalogAwareKnowledgeStore(
        IKnowledgeStore inner,
        IKnowledgeCatalog catalog,
        CatalogKeywordExtractor? extractor = null,
        TimeDecayOptions? decayOptions = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(catalog);

        _inner = inner;
        _catalog = catalog;
        _extractor = extractor;
        _decayOptions = decayOptions;
    }

    /// <summary>The underlying catalog maintained by this decorator.</summary>
    public IKnowledgeCatalog Catalog => _catalog;

    /// <inheritdoc />
    public async Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(
        string query, SearchOptions? options = null, CancellationToken ct = default)
    {
        var results = await _inner.SearchAsync(query, options, ct);

        if (_decayOptions is null || results.Count == 0)
            return results;

        // Apply time-decay reranking using catalog timestamps
        var sourceCache = new Dictionary<string, CatalogEntry?>();
        var reranked = new List<KnowledgeChunk>(results.Count);

        foreach (var chunk in results)
        {
            var source = chunk.Metadata.GetValueOrDefault("source");
            if (source is null)
            {
                reranked.Add(chunk);
                continue;
            }

            if (!sourceCache.TryGetValue(source, out var entry))
            {
                entry = await _catalog.GetAsync(source, ct);
                sourceCache[source] = entry;
            }

            if (entry is null)
            {
                reranked.Add(chunk);
                continue;
            }

            var decayedScore = TimeDecay.Apply(chunk.Score, entry.IndexedAt, _decayOptions);
            reranked.Add(chunk with { Score = decayedScore });
        }

        reranked.Sort((a, b) => b.Score.CompareTo(a.Score));
        return reranked;
    }

    /// <inheritdoc />
    public async Task UpsertAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken ct = default)
    {
        var docList = documents.ToList();
        await _inner.UpsertAsync(docList, ct);

        if (docList.Count == 0) return;

        // Group by source and update catalog for each
        var groups = docList
            .Where(d => d.Metadata.ContainsKey("source"))
            .GroupBy(d => d.Metadata["source"]);

        foreach (var group in groups)
        {
            var source = group.Key;
            var chunks = group.ToList();

            // Build representative text from the first few chunks for enrichment
            var representativeText = string.Join("\n\n",
                chunks.Take(3).Select(d => d.Text));

            CatalogEnrichment? enrichment = null;
            if (_extractor is not null)
                enrichment = await _extractor.ExtractAsync(representativeText, ct);

            var entry = new CatalogEntry
            {
                Source = source,
                Summary = enrichment?.Summary ?? string.Empty,
                Keywords = enrichment?.Keywords ?? [],
                Category = enrichment?.Category ?? string.Empty,
                IndexedAt = DateTimeOffset.UtcNow,
                ChunkCount = chunks.Count
            };

            await _catalog.IndexAsync(entry, ct);
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(KnowledgeFilter filter, CancellationToken ct = default)
    {
        await _inner.DeleteAsync(filter, ct);

        // If filter targets a specific source, remove its catalog entry
        if (filter.TryGetValue("source", out var source))
            await _catalog.RemoveAsync(source, ct);
    }
}
