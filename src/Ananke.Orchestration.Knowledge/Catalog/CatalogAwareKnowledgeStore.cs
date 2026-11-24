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
/// <see cref="KnowledgeFilter.AnyOf"/>, the corresponding catalog entries are removed. When another key narrows the
/// delete (a <c>page</c> as well as a <c>source</c>), a source's entry stays while chunks of it remain, and goes with
/// its last chunk; over an inner store that is not an <see cref="IListableKnowledgeStore"/>, or wraps one that can't
/// list, the entries of every source the filter names are removed. When a superseded source is deleted while a newer
/// document still lists it in its <see cref="CatalogEntry.Replaces"/>, an empty entry keeps it superseded by that one,
/// so deleting a document before indexing it again doesn't lose the mark. A deleted document's own
/// <see cref="CatalogEntry.Replaces"/> marks are cleared from the sources it replaced, as are those of a document
/// indexed again on the sources it no longer lists, unless a source's own header names it as what supersedes it (its
/// chunks carry <c>superseded_by</c>). Telling the two apart takes listing the source's chunks: over an inner store
/// that is not an <see cref="IListableKnowledgeStore"/>, or wraps one that can't list, those marks stay. A source
/// another document still lists in its <see cref="CatalogEntry.Replaces"/> is marked superseded by that one instead
/// (<see cref="IKnowledgeCatalog.GetReplacingAsync"/>).
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

    /// <summary>The store this one decorates.</summary>
    internal IKnowledgeStore Inner => _inner;

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
    /// The chunks the store this one wraps lists for <paramref name="filter"/>, at most <paramref name="limit"/>, marked
    /// from the catalog as <see cref="SearchAsync"/> marks its results, and under <see cref="SupersededResults.Exclude"/>
    /// without the superseded ones; <see langword="null"/> when that store can't list.
    /// </summary>
#pragma warning disable ANNKEEXP004 // reads the experimental SupersededResults
    internal async Task<IReadOnlyList<KnowledgeChunk>?> TryListMarkedAsync(KnowledgeFilter filter, int limit, CancellationToken ct)
    {
        if (await _inner.TryListAsync(filter, limit, ct).ConfigureAwait(false) is not { } listed)
            return null;
        var marked = await MarkAsync(listed, new Dictionary<string, CatalogEntry?>(StringComparer.Ordinal), ct).ConfigureAwait(false);
        return SupersededResults == SupersededResults.Exclude ? [.. marked.Where(c => !IsSuperseded(c))] : marked;
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

        // One catalog read for the sources this round brings that earlier rounds didn't.
        var unread = results.Select(Source).OfType<string>().Where(s => !entries.ContainsKey(s)).ToList();
        if (unread.Count > 0)
        {
            foreach (var source in unread)
                entries[source] = null;
            foreach (var found in await _catalog.GetManyAsync(unread, ct).ConfigureAwait(false))
                entries[found.Source] = found;
        }

        var now = _timeProvider.GetUtcNow();
        var marked = new List<KnowledgeChunk>(results.Count);
        foreach (var chunk in results)
        {
            if (Source(chunk) is not { } source || entries[source] is not { } entry)
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

    /// <summary>The chunk's <c>source</c>, or <see langword="null"/> when it is blank: a catalog has no entry for one, and rejects it.</summary>
    private static string? Source(KnowledgeChunk chunk) =>
        chunk.Metadata.GetValueOrDefault("source") is { } source && !string.IsNullOrWhiteSpace(source) ? source : null;

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
            if (existing is not null)
                await ClearMarksAsync(source, [.. existing.Replaces.Except(replaces, StringComparer.Ordinal)], ct);
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
        KnowledgeFilter.ThrowIfEmptyForDelete(filter);
        await _inner.DeleteAsync(filter, ct);

        // A filter that targets sources removes their catalog entries, but one that another key narrows only those of
        // sources it took the last chunk of.
#pragma warning disable ANNKEEXP004 // reads the experimental AnyOf
        var narrowed = filter.Keys.Any(k => k != "source") || filter.AnyOf.Keys.Any(k => k != "source");
#pragma warning restore ANNKEEXP004
        foreach (var source in filter.ValuesOf("source") ?? [])
        {
            if (narrowed && await HasChunksAsync(source, ct).ConfigureAwait(false))
                continue;
            await RemoveEntryAsync(source, ct);
        }
    }

    /// <summary>
    /// Whether the inner store still holds a chunk of <paramref name="source"/>; <see langword="false"/> when it can't
    /// list.
    /// </summary>
    private async Task<bool> HasChunksAsync(string source, CancellationToken ct) =>
        await _inner.TryListAsync(new KnowledgeFilter { ["source"] = source }, limit: 1, ct).ConfigureAwait(false) is { Count: > 0 };

    /// <summary>
    /// Removes a source's catalog entry. A superseded source keeps a mark on an empty entry while a newer document still
    /// says it replaces this one (<see cref="ReplacedByAsync"/>), so deleting before indexing again doesn't lose it. The
    /// marks this document left on the ones it replaces are cleared (<see cref="ClearMarksAsync"/>).
    /// </summary>
    private async Task RemoveEntryAsync(string source, CancellationToken ct)
    {
        var existing = await _catalog.GetAsync(source, ct).ConfigureAwait(false);
        await _catalog.RemoveAsync(source, ct).ConfigureAwait(false);
        if (existing is not null)
            await ClearMarksAsync(source, existing.Replaces, ct).ConfigureAwait(false);
        if (existing?.SupersededBy is { Length: > 0 } marked
            && await ReplacedByAsync(source, preferred: marked, except: null, ct).ConfigureAwait(false) is { } newer)
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

    /// <summary>
    /// Clears the mark <paramref name="newer"/> left on each source it replaced, unless a chunk of that source carries
    /// <c>superseded_by</c> naming it (its own header said so), which only a store that lists can show. Over a store
    /// that can't list, the marks stay. Where another document still lists the source in its
    /// <see cref="CatalogEntry.Replaces"/>, the mark names that one instead (<see cref="ReplacedByAsync"/>); else an
    /// empty entry that only carried the mark is removed.
    /// </summary>
    private async Task ClearMarksAsync(string newer, IReadOnlyList<string> replaced, CancellationToken ct)
    {
        foreach (var source in replaced.Where(s => s != newer).Distinct(StringComparer.Ordinal))
        {
            if (await _catalog.GetAsync(source, ct).ConfigureAwait(false) is not { } entry || entry.SupersededBy != newer)
                continue;

            var named = await _inner.TryListAsync(
                new KnowledgeFilter { ["source"] = source, [SupersededByKey] = newer }, limit: 1, ct).ConfigureAwait(false);
            if (named is null)
                return;
            if (named.Count > 0)
                continue;

            if (await ReplacedByAsync(source, preferred: null, except: newer, ct).ConfigureAwait(false) is { } other)
                await _catalog.IndexAsync(entry with { SupersededBy = other }, ct).ConfigureAwait(false);
            else if (entry.ChunkCount == 0)
                await _catalog.RemoveAsync(source, ct).ConfigureAwait(false);
            else
                await _catalog.IndexAsync(entry with { SupersededBy = null }, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The document, other than <paramref name="except"/>, that still lists <paramref name="source"/> in its
    /// <see cref="CatalogEntry.Replaces"/>: <paramref name="preferred"/> when it is one, else the newest by its own date
    /// or else when it was indexed; <see langword="null"/> when none does.
    /// </summary>
    private async Task<string?> ReplacedByAsync(string source, string? preferred, string? except, CancellationToken ct)
    {
        var replacing = (await _catalog.GetReplacingAsync(source, ct).ConfigureAwait(false))
            .Where(e => e.Source != source && e.Source != except)
            .ToList();
        return replacing.Any(e => e.Source == preferred)
            ? preferred
            : replacing.OrderByDescending(e => e.EffectiveAt ?? e.IndexedAt).ThenBy(e => e.Source, StringComparer.Ordinal).FirstOrDefault()?.Source;
    }

    /// <inheritdoc />
#pragma warning disable ANNKEEXP004 // lists through the experimental listing
    public Task<IReadOnlyList<KnowledgeChunk>> ListAsync(
        KnowledgeFilter filter, int limit = 1000, KnowledgeDateOrder? order = null, CancellationToken ct = default) =>
        _inner.ListThroughAsync(filter, limit, order, ct);

    /// <inheritdoc />
    /// <remarks>Through the inner store; the catalog is left as it is.</remarks>
    public Task UpdateMetadataAsync(KnowledgeChunk listed, IReadOnlyDictionary<string, string> metadata, CancellationToken ct = default) =>
        _inner.UpdateMetadataThroughAsync(listed, metadata, ct);
#pragma warning restore ANNKEEXP004

    /// <summary>The document's own date, from its chunks' <c>effective_at</c> metadata, when it is there and parses.</summary>
    private static DateTimeOffset? EffectiveAt(IEnumerable<KnowledgeDocument> chunks) =>
        chunks.Select(c => c.Metadata.TryGetValue(EffectiveAtKey, out var value) ? value : null)
            .FirstOrDefault(v => v is not null) is { } text
        && DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;
}
