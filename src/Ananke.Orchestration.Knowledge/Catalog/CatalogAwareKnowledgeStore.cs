using System.Diagnostics.CodeAnalysis;

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
/// On <see cref="DeleteAsync"/>: if the filter targets a specific <c>source</c>, or several through
/// <see cref="KnowledgeFilter.AnyOf"/>, the corresponding catalog entries are removed. When a newer document that
/// still lists a source in its <see cref="CatalogEntry.Replaces"/> had marked it superseded, an empty entry keeps that
/// mark.
/// </para>
/// <para>
/// On <see cref="SearchAsync"/>: each result's metadata gets its document's date and what supersedes it from the
/// catalog entry, where the chunk has none of its own (see <see cref="SupersededResults"/>). Results are reranked using time-decay
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
    /// Under <see cref="Catalog.SupersededResults.Exclude"/>, the most results the store is asked for, as a multiple of
    /// <see cref="SearchOptions.TopK"/>.
    /// </summary>
    private const int ExcludeCandidatesPerResult = 16;

#pragma warning disable ANNKEEXP004 // the metadata keys DocumentHeaderOptions writes a document's date and superseding under
    private const string EffectiveAtKey = Documents.DocumentHeaderOptions.EffectiveAtKey;
    private const string SupersededByKey = Documents.DocumentHeaderOptions.SupersededByKey;
#pragma warning restore ANNKEEXP004

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

    /// <summary>
    /// What <see cref="SearchAsync"/> does with a result whose document is superseded. Default is
    /// <see cref="Catalog.SupersededResults.Mark"/>: every result is marked, and the ranking is unchanged.
    /// </summary>
    [Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
    public SupersededResults SupersededResults { get; init; } = SupersededResults.Mark;

    /// <inheritdoc />
    /// <remarks>
    /// Each result's metadata gets its document's date (<c>effective_at</c>) and what supersedes it
    /// (<c>superseded_by</c>) from the catalog entry when the chunk doesn't carry them, so a passage of a document only a
    /// newer one says it replaces comes back marked. <see cref="SupersededResults"/> says whether superseded results
    /// are demoted or left out.
    /// </remarks>
#pragma warning disable ANNKEEXP004 // reads the experimental SupersededResults
    public async Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(
        string query, SearchOptions? options = null, CancellationToken ct = default)
    {
        options ??= new SearchOptions();
        var entries = new Dictionary<string, CatalogEntry?>(StringComparer.Ordinal);
        var asked = options;
        while (true)
        {
            var results = await _inner.SearchAsync(query, asked, ct).ConfigureAwait(false);
            var marked = await MarkAsync(results, entries, ct).ConfigureAwait(false);
            if (SupersededResults != SupersededResults.Exclude)
                return SupersededResults == SupersededResults.Demote ? [.. marked.OrderBy(IsSuperseded)] : marked;

            // Left out: ask the store for more until TopK current results are found, it has no more to give, or it has
            // been asked for ExcludeCandidatesPerResult times TopK.
            var current = marked.Where(c => !IsSuperseded(c)).ToList();
            if (current.Count >= options.TopK || results.Count < asked.TopK
                || asked.TopK >= (long)options.TopK * ExcludeCandidatesPerResult)
                return [.. current.Take(options.TopK)];
            asked = asked with { TopK = asked.TopK * 2 };
        }
    }
#pragma warning restore ANNKEEXP004

    /// <summary>
    /// The results with their documents' dates and superseding written in from the catalog, reranked by time decay when
    /// it is set.
    /// </summary>
    private async Task<IReadOnlyList<KnowledgeChunk>> MarkAsync(
        IReadOnlyList<KnowledgeChunk> results, Dictionary<string, CatalogEntry?> entries, CancellationToken ct)
    {
        if (results.Count == 0)
            return results;

        var now = _timeProvider.GetUtcNow();
        var marked = new List<KnowledgeChunk>(results.Count);
        foreach (var chunk in results)
        {
            // A catalog has no entry for a blank source, and its GetAsync rejects one.
            if (chunk.Metadata.GetValueOrDefault("source") is not { } source || string.IsNullOrWhiteSpace(source))
            {
                marked.Add(chunk);
                continue;
            }

            if (!entries.TryGetValue(source, out var entry))
            {
                entry = await _catalog.GetAsync(source, ct).ConfigureAwait(false);
                entries[source] = entry;
            }

            if (entry is null)
            {
                marked.Add(chunk);
                continue;
            }

            var withEntry = WithEntry(chunk, entry);
            marked.Add(_decayOptions is null
                ? withEntry
                : withEntry with { Score = TimeDecay.Apply(chunk.Score, entry.EffectiveAt ?? entry.IndexedAt, now, _decayOptions) });
        }

        if (_decayOptions is not null)
            marked.Sort((a, b) => b.Score.CompareTo(a.Score));
        return marked;
    }

    /// <summary>The chunk with its catalog entry's <c>effective_at</c> and <c>superseded_by</c>, where the chunk has none of its own.</summary>
    private static KnowledgeChunk WithEntry(KnowledgeChunk chunk, CatalogEntry entry)
    {
        var date = entry.EffectiveAt is { } at && !HasValue(chunk, EffectiveAtKey)
            ? at.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            : null;
        var supersededBy = entry.SupersededBy is { Length: > 0 } by && !HasValue(chunk, SupersededByKey) ? by : null;
        if (date is null && supersededBy is null)
            return chunk;

        var metadata = new Dictionary<string, string>(chunk.Metadata);
        if (date is not null)
            metadata[EffectiveAtKey] = date;
        if (supersededBy is not null)
            metadata[SupersededByKey] = supersededBy;
        return chunk with { Metadata = metadata };
    }

    private static bool IsSuperseded(KnowledgeChunk chunk) => HasValue(chunk, SupersededByKey);

    private static bool HasValue(KnowledgeChunk chunk, string key) =>
        chunk.Metadata.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value);

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
                SupersededBy = chunks.Select(c => c.Metadata.TryGetValue(SupersededByKey, out var by) ? by : null)
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

        // A filter that targets sources removes their catalog entries.
        foreach (var source in filter.ValuesOf("source") ?? [])
            await RemoveEntryAsync(source, ct);
    }

    /// <summary>
    /// Removes a source's catalog entry. A newer document's mark is kept on an empty entry while that document still
    /// says it replaces this one, so deleting before indexing again doesn't lose it.
    /// </summary>
    private async Task RemoveEntryAsync(string source, CancellationToken ct)
    {
        var existing = await _catalog.GetAsync(source, ct).ConfigureAwait(false);
        await _catalog.RemoveAsync(source, ct).ConfigureAwait(false);
        if (existing?.SupersededBy is { Length: > 0 } newer
            && await _catalog.GetAsync(newer, ct).ConfigureAwait(false) is { } replacing
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
            }, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
#pragma warning disable ANNKEEXP004 // lists through the experimental listing
    public Task<IReadOnlyList<KnowledgeChunk>> ListAsync(
        KnowledgeFilter filter, int limit = 1000, KnowledgeDateOrder? order = null, CancellationToken ct = default) =>
        _inner is IListableKnowledgeStore listable
            ? listable.ListAsync(filter, limit, order, ct)
            : throw new NotSupportedException($"The store this one wraps ({_inner.GetType().Name}) can't list.");
#pragma warning restore ANNKEEXP004

    /// <summary>The document's own date, from its chunks' <c>effective_at</c> metadata, when it is there and parses.</summary>
    private static DateTimeOffset? EffectiveAt(IEnumerable<KnowledgeDocument> chunks) =>
        chunks.Select(c => c.Metadata.TryGetValue(EffectiveAtKey, out var value) ? value : null)
            .FirstOrDefault(v => v is not null) is { } text
        && DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var date)
            ? date
            : null;
}
