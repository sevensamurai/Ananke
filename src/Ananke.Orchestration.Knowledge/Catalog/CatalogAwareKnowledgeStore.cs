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
/// corresponding catalog entry is removed. When a newer document that still lists the source in its
/// <see cref="CatalogEntry.Replaces"/> had marked it superseded, an empty entry keeps that mark.
/// </para>
/// <para>
/// On <see cref="SearchAsync"/>: results from the inner store are reranked using time-decay
/// when <see cref="TimeDecayOptions"/> are configured. Each result's score is multiplied by
/// a weight derived from its source document's age: from <see cref="CatalogEntry.EffectiveAt"/>, the document's own
/// date, when it has one, else from <see cref="CatalogEntry.IndexedAt"/>. On upsert, a chunk's <c>effective_at</c>
/// metadata (written by <see cref="Documents.DocumentProcessor"/>'s header reading) becomes the entry's
/// <see cref="CatalogEntry.EffectiveAt"/>.
/// </para>
/// </remarks>
#pragma warning disable ANNKEEXP004 // implements the experimental listing
public sealed class CatalogAwareKnowledgeStore : IListableKnowledgeStore
#pragma warning restore ANNKEEXP004
{
    private readonly IKnowledgeStore _inner;
    private readonly IKnowledgeCatalog _catalog;
    private readonly CatalogKeywordExtractor? _extractor;
    private readonly TimeDecayOptions? _decayOptions;
    private readonly TimeProvider _timeProvider;

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
    /// <param name="timeProvider">
    /// Clock that stamps catalog entries and measures a document's age for decay.
    /// Defaults to <see cref="TimeProvider.System"/>.
    /// </param>
    public CatalogAwareKnowledgeStore(
        IKnowledgeStore inner,
        IKnowledgeCatalog catalog,
        CatalogKeywordExtractor? extractor = null,
        TimeDecayOptions? decayOptions = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(catalog);

        _inner = inner;
        _catalog = catalog;
        _extractor = extractor;
        _decayOptions = decayOptions;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
        var now = _timeProvider.GetUtcNow();
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

            var decayedScore = TimeDecay.Apply(chunk.Score, entry.EffectiveAt ?? entry.IndexedAt, now, _decayOptions);
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

            // A document's own header says what supersedes it; failing that, a newer document may already have said so,
            // and that mark stays when this one is indexed again.
            var existing = await _catalog.GetAsync(source, ct);
            var replaces = Replaces(chunks);
            var entry = new CatalogEntry
            {
                Source = source,
                Summary = enrichment?.Summary ?? string.Empty,
                Keywords = enrichment?.Keywords ?? [],
                Category = enrichment?.Category ?? string.Empty,
                IndexedAt = _timeProvider.GetUtcNow(),
                EffectiveAt = EffectiveAt(chunks),
                SupersededBy = chunks.Select(c => c.Metadata.TryGetValue("superseded_by", out var by) ? by : null)
                    .FirstOrDefault(by => !string.IsNullOrEmpty(by)) ?? existing?.SupersededBy,
                Replaces = replaces,
                Fields = HeaderFields(chunks),
                ChunkCount = chunks.Count
            };

            await _catalog.IndexAsync(entry, ct);
            await MarkReplacedAsync(source, replaces, ct);
        }
    }

    /// <summary>
    /// Marks each source <paramref name="newer"/> replaces as superseded by it, unless something already supersedes it.
    /// A source not indexed yet gets an empty entry that carries the mark until its document comes.
    /// </summary>
    private async Task MarkReplacedAsync(string newer, IReadOnlyList<string> replaced, CancellationToken ct)
    {
        foreach (var source in replaced.Where(s => s != newer).Distinct(StringComparer.Ordinal))
        {
            var existing = await _catalog.GetAsync(source, ct).ConfigureAwait(false);
            if (existing?.SupersededBy is not null)
                continue;

            await _catalog.IndexAsync(existing is null
                ? new CatalogEntry
                {
                    Source = source,
                    Summary = string.Empty,
                    Keywords = [],
                    IndexedAt = _timeProvider.GetUtcNow(),
                    ChunkCount = 0,
                    SupersededBy = newer,
                }
                : existing with { SupersededBy = newer }, ct).ConfigureAwait(false);
        }
    }

    /// <summary>The header fields ingestion kept, named by the first chunk's <c>header_fields</c> metadata (a JSON array).</summary>
    private static IReadOnlyDictionary<string, string> HeaderFields(IReadOnlyList<KnowledgeDocument> chunks)
    {
        var first = chunks.FirstOrDefault(c => c.Metadata.ContainsKey("header_fields"));
        if (first is null)
            return new Dictionary<string, string>();
        try
        {
            var names = System.Text.Json.JsonSerializer.Deserialize<string[]>(first.Metadata["header_fields"]) ?? [];
            return names.Where(first.Metadata.ContainsKey).ToDictionary(n => n, n => first.Metadata[n], StringComparer.Ordinal);
        }
        catch (System.Text.Json.JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>The sources a document replaces, from its chunks' <c>replaces_sources</c> metadata (a JSON array).</summary>
    private static IReadOnlyList<string> Replaces(IEnumerable<KnowledgeDocument> chunks)
    {
        var json = chunks.Select(c => c.Metadata.TryGetValue("replaces_sources", out var value) ? value : null).FirstOrDefault(v => v is not null);
        if (json is null)
            return [];
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(KnowledgeFilter filter, CancellationToken ct = default)
    {
        await _inner.DeleteAsync(filter, ct);

        // If filter targets a specific source, remove its catalog entry. A newer document's mark is kept on an empty
        // entry while that document still says it replaces this one, so deleting before indexing again doesn't lose it.
        if (!filter.TryGetValue("source", out var source))
            return;

        var existing = await _catalog.GetAsync(source, ct);
        await _catalog.RemoveAsync(source, ct);
        if (existing?.SupersededBy is { Length: > 0 } newer
            && await _catalog.GetAsync(newer, ct) is { } replacing
            && replacing.Replaces.Contains(source, StringComparer.Ordinal))
        {
            await _catalog.IndexAsync(new CatalogEntry
            {
                Source = source,
                Summary = string.Empty,
                Keywords = [],
                IndexedAt = _timeProvider.GetUtcNow(),
                ChunkCount = 0,
                SupersededBy = newer,
            }, ct);
        }
    }

    /// <inheritdoc />
#pragma warning disable ANNKEEXP004 // lists through the experimental listing
    public Task<IReadOnlyList<KnowledgeChunk>> ListAsync(KnowledgeFilter filter, int limit = 1000, CancellationToken ct = default) =>
        _inner is IListableKnowledgeStore listable
            ? listable.ListAsync(filter, limit, ct)
            : throw new NotSupportedException($"The store this one wraps ({_inner.GetType().Name}) can't list.");
#pragma warning restore ANNKEEXP004

    /// <summary>The document's own date, from its chunks' <c>effective_at</c> metadata, when it is there and parses.</summary>
    private static DateTimeOffset? EffectiveAt(IEnumerable<KnowledgeDocument> chunks) =>
        chunks.Select(c => c.Metadata.TryGetValue("effective_at", out var value) ? value : null)
            .FirstOrDefault(v => v is not null) is { } text
        && DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var date)
            ? date
            : null;
}
