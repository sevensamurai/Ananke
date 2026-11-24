using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Ananke.Orchestration.Knowledge.Catalog;
using Ananke.Orchestration.Knowledge.Documents;

namespace Ananke.Orchestration.Knowledge.Records;

/// <summary>A record found by <see cref="DatedRecordReader{TRecord}"/>, with its date and what, if anything, replaced it.</summary>
/// <param name="Record">The product's record.</param>
/// <param name="Id">
/// Its id in the store, <c>{source}:record:{n}</c> (<c>{source}:record:{type}:{n}</c> with a record type), or the id it
/// was written under.
/// </param>
/// <param name="Source">The document it was read from.</param>
/// <param name="Text">Its text, as embedded and searched.</param>
/// <param name="EffectiveAt">Its date: its own, else its document's; <see langword="null"/> when neither had one.</param>
/// <param name="Replaces">What it says it replaces, if anything.</param>
/// <param name="ReplacedBy">
/// What replaced it: the id of a newer record returned with it that names it, or what supersedes its document; else
/// <see langword="null"/>.
/// </param>
/// <param name="Score">The store's score for it, in the search mode asked for.</param>
/// <param name="Quote">Its quoted passage, when its extractor asked for one (<see cref="DatedRecordOptions{TRecord}.Quote"/>).</param>
/// <param name="Section">The title of the section its quote was found in, when that section has one.</param>
/// <param name="PassageGone">
/// Whether it is a written record (<see cref="DatedRecordWriter{TRecord}"/>) whose quote wasn't in its document when the
/// document was last indexed.
/// </param>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed record DatedRecord<TRecord>(
    TRecord Record, string Id, string Source, string Text, DateTimeOffset? EffectiveAt, string? Replaces, string? ReplacedBy, float Score,
    string? Quote = null, string? Section = null, bool PassageGone = false);

/// <summary>
/// Searches the records a <see cref="DatedRecordExtractor{TRecord}"/> or a <see cref="DatedRecordWriter{TRecord}"/>
/// stored, and says which are replaced. Replacement is
/// explicit only: a record is replaced when its document's header says the document is superseded
/// (<see cref="DocumentHeaderOptions.SupersededByKey"/>), or when a newer record returned with it names it in its
/// <c>replaces</c>: every word of three or more letters there appears in the older record's text, ignoring case.
/// Nothing is matched by similarity.
/// </summary>
/// <remarks>
/// The store picks which records match (<see cref="SearchOptions.TopK"/> of them); they are returned newest first, those
/// without a date last.
/// </remarks>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed partial class DatedRecordReader<TRecord>
{
    private readonly IKnowledgeStore _records;
    private readonly IKnowledgeCatalog? _catalog;
    private readonly string? _recordType;

    /// <summary>Reads records from <paramref name="records"/>, the store a <see cref="DatedRecordExtractor{TRecord}"/> writes.</summary>
    /// <param name="records">The records' store.</param>
    /// <param name="catalog">The catalog that says which documents are superseded, if any.</param>
    /// <param name="recordType">
    /// The record type to read, as its extractor names it (<see cref="DatedRecordOptions{TRecord}.RecordType"/>), when
    /// the store keeps several; every search and list is then limited to it. Without it, every record in the store is
    /// read as a <typeparamref name="TRecord"/>.
    /// </param>
    public DatedRecordReader(IKnowledgeStore records, IKnowledgeCatalog? catalog = null, string? recordType = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (recordType is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(recordType);
        _records = records;
        _catalog = catalog;
        _recordType = recordType;
    }

    /// <summary>The records matching <paramref name="query"/>, newest first, each labelled with what replaced it.</summary>
    public async Task<IReadOnlyList<DatedRecord<TRecord>>> SearchAsync(
        string query, SearchOptions? options = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (_recordType is not null)
            options = (options ?? new SearchOptions()) with { Filter = OfType(options?.Filter) };
        var chunks = await _records.SearchAsync(query, options, ct).ConfigureAwait(false);
        return await LabelAsync(chunks, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The newest <paramref name="limit"/> records whose metadata matches <paramref name="filter"/> (a property kept by
    /// <see cref="DatedRecordOptions{TRecord}.Fields"/>, a source), dated <paramref name="since"/> or later when given,
    /// newest first and labelled as <see cref="SearchAsync"/> labels them. The store orders them by date and cuts them
    /// at the limit; records without a date come last, and are left out when <paramref name="since"/> is given. A list
    /// of <paramref name="limit"/> records may have left more out.
    /// </summary>
    /// <exception cref="NotSupportedException">The records' store can't list (<see cref="IListableKnowledgeStore"/>).</exception>
    public async Task<IReadOnlyList<DatedRecord<TRecord>>> ListAsync(
        KnowledgeFilter? filter = null, DateTimeOffset? since = null, int limit = 1000, CancellationToken ct = default)
    {
        if (_records is not IListableKnowledgeStore listable)
            throw new NotSupportedException($"The records' store ({_records.GetType().Name}) can't list.");

        var chunks = await listable.ListAsync(
            OfType(filter) ?? new KnowledgeFilter(), limit, new KnowledgeDateOrder(DocumentHeaderOptions.EffectiveAtKey, since), ct)
            .ConfigureAwait(false);
        return await LabelAsync(chunks, ct).ConfigureAwait(false);
    }

    /// <summary><paramref name="filter"/> limited to the reader's record type, if it has one, as a copy: the caller's is left as it is.</summary>
    private KnowledgeFilter? OfType(KnowledgeFilter? filter)
    {
        if (_recordType is null)
            return filter;

        var typed = filter?.Copy() ?? new KnowledgeFilter();
        typed[DatedRecordExtractor<TRecord>.RecordTypeKey] = _recordType;
        return typed;
    }

    /// <summary>Records from <paramref name="chunks"/>, newest first, each labelled with what replaced it.</summary>
    private async Task<IReadOnlyList<DatedRecord<TRecord>>> LabelAsync(IReadOnlyList<KnowledgeChunk> chunks, CancellationToken ct)
    {
        var found = chunks
            .Select(c => (Chunk: c, Record: DatedRecordExtractor<TRecord>.Read(c)))
            .Where(r => r.Record is not null)
            .Select(r => new DatedRecord<TRecord>(
                r.Record!,
                r.Chunk.Id,
                r.Chunk.Metadata.GetValueOrDefault("source", ""),
                r.Chunk.Text,
                Date(r.Chunk),
                r.Chunk.Metadata.GetValueOrDefault(DatedRecordExtractor<TRecord>.ReplacesKey),
                r.Chunk.Metadata.GetValueOrDefault(DocumentHeaderOptions.SupersededByKey),
                r.Chunk.Score,
                r.Chunk.Metadata.GetValueOrDefault(DatedRecordExtractor<TRecord>.QuoteKey),
                r.Chunk.Metadata.GetValueOrDefault(DatedRecordExtractor<TRecord>.SectionKey),
                r.Chunk.Metadata.ContainsKey(DatedRecordWriter<TRecord>.PassageGoneKey)))
            .OrderByDescending(r => r.EffectiveAt.HasValue)
            .ThenByDescending(r => r.EffectiveAt)
            .ToList();

        // What supersedes a whole document, from the catalog when there is one: a newer document may have said so. A
        // record without a source has no catalog entry, and the catalog rejects a blank source.
        var supersededSources = new Dictionary<string, string?>(StringComparer.Ordinal);
        var sources = found.Where(r => r.ReplacedBy is null && !string.IsNullOrWhiteSpace(r.Source)).Select(r => r.Source).ToList();
        if (_catalog is not null && sources.Count > 0)
        {
            foreach (var entry in await _catalog.GetManyAsync(sources, ct).ConfigureAwait(false))
                supersededSources[entry.Source] = entry.SupersededBy;
        }

        return [.. found.Select(record => record.ReplacedBy is not null
            ? record
            : record with
            {
                ReplacedBy = supersededSources.GetValueOrDefault(record.Source)
                    ?? found.FirstOrDefault(newer => Replaces(newer, record))?.Id,
            })];
    }

    /// <summary>Whether <paramref name="newer"/> is dated after <paramref name="older"/> and names it in its <c>replaces</c>.</summary>
    private static bool Replaces(DatedRecord<TRecord> newer, DatedRecord<TRecord> older)
    {
        if (newer.Replaces is null || newer.EffectiveAt is not { } newerDate || older.EffectiveAt is { } olderDate && olderDate >= newerDate)
            return false;

        var named = Words(newer.Replaces);
        var text = Words(older.Text).ToHashSet();
        return named.Count > 0 && named.All(text.Contains);
    }

    private static List<string> Words(string text) =>
        [.. Word().Matches(text.ToLowerInvariant()).Select(m => m.Value).Distinct()];

    private static DateTimeOffset? Date(KnowledgeChunk chunk) =>
        KnowledgeDateOrder.ReadDate(chunk.Metadata.GetValueOrDefault(DocumentHeaderOptions.EffectiveAtKey));

    [GeneratedRegex(@"[\p{L}\p{N}]{3,}")]
    private static partial Regex Word();
}
