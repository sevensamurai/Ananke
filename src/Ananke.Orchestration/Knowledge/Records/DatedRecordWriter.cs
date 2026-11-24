using System.Diagnostics.CodeAnalysis;
using Ananke.Orchestration.Knowledge.Documents;

namespace Ananke.Orchestration.Knowledge.Records;

/// <summary>
/// Stores records given to it rather than read by a model, such as people's notes on a document, in the store a
/// <see cref="DatedRecordExtractor{TRecord}"/> writes: a <see cref="DatedRecordReader{TRecord}"/> returns them beside the
/// extracted records, and an extractor indexing their document again leaves them alone. Give it to
/// <see cref="DocumentProcessor.Records"/> as well (in a <see cref="CompositeDocumentRecords"/> beside the extractors), and
/// each time a document is indexed again it checks the quotes of the records written for it.
/// </summary>
/// <remarks>
/// <para>
/// A record is stored under the id it is given, with the metadata an extractor gives a record from the same
/// <see cref="DatedRecordOptions{TRecord}"/> (no header fields: its date is its own, if it has one), its quote, trimmed,
/// under <see cref="DatedRecordExtractor{TRecord}.QuoteKey"/> when <see cref="DatedRecordOptions{TRecord}.Quote"/> gives
/// one (with <see cref="DatedRecordOptions{TRecord}.Quote"/> set, a <c>quote</c> that
/// <see cref="DatedRecordOptions{TRecord}.Fields"/> gives isn't stored), and <see cref="WrittenKey"/>.
/// <see cref="DatedRecordOptions{TRecord}.Instructions"/>,
/// <see cref="DatedRecordOptions{TRecord}.Keep"/> and the model limits aren't used.
/// </para>
/// <para>
/// A written record is never deleted by Ananke: when its document is indexed again and its quote is in none of the
/// document's sections (matched as an extractor matches quotes), it is marked with <see cref="PassageGoneKey"/>, and
/// the mark is taken off once the quote is there again. A record with no quote is never marked. The product decides
/// what to show or delete; a store delete whose filter has <see cref="WrittenKey"/> removes written records only.
/// </para>
/// <para>
/// A mark changes only the record's metadata, through <see cref="IListableKnowledgeStore.UpdateMetadataAsync"/>: its
/// text isn't embedded again, and a record written again or deleted (an erasure) while its document is indexed keeps
/// that write or stays deleted.
/// </para>
/// </remarks>
/// <typeparam name="TRecord">The product's record type.</typeparam>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class DatedRecordWriter<TRecord> : IDocumentRecords
{
    /// <summary>The metadata key a written record carries, with the value <c>true</c>.</summary>
    public const string WrittenKey = "written";

    /// <summary>
    /// The metadata key, with the value <c>true</c>, of a written record whose quote wasn't in its document when the
    /// document was last indexed (<see cref="DatedRecord{TRecord}.PassageGone"/>).
    /// </summary>
    public const string PassageGoneKey = "passage_gone";

    private readonly IKnowledgeStore _store;
    private readonly DatedRecordOptions<TRecord> _options;

    /// <summary>Stores records in <paramref name="store"/> as <paramref name="options"/> say.</summary>
    public DatedRecordWriter(IKnowledgeStore store, DatedRecordOptions<TRecord> options)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        if (options.RecordType is { } recordType)
            ArgumentException.ThrowIfNullOrWhiteSpace(recordType, nameof(options));
        _store = store;
        _options = options;
    }

    /// <inheritdoc />
    /// <remarks>None: a writer deletes no records, so it can share a store with any extractor.</remarks>
    public IReadOnlyList<(IKnowledgeStore Store, string? RecordType)> Owns => [];

    /// <inheritdoc />
    /// <remarks>Its store, where it marks the records written for a source.</remarks>
    public IReadOnlyList<IKnowledgeStore> WritesTo => [_store];

    /// <summary>
    /// Stores <paramref name="record"/> for <paramref name="sourceId"/> as <paramref name="id"/>, replacing the record
    /// stored under that id, mark included. Give an id of your own, not one of the form an extractor gives
    /// (<c>{source}:record:…</c>), which the extractor would overwrite.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="id"/> is of the form an extractor gives for <paramref name="sourceId"/>.</exception>
    public async Task WriteAsync(string sourceId, string id, TRecord record, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(record);
        if (id.StartsWith($"{sourceId}:record:", StringComparison.Ordinal))
            throw new ArgumentException($"'{id}' is of the form an extractor gives its records, which it would overwrite.", nameof(id));

        var metadata = DatedRecordExtractor<TRecord>.Metadata(_options, sourceId, record, new Dictionary<string, string>());
        metadata[WrittenKey] = "true";
        if (_options.Quote?.Invoke(record) is { } quote && !string.IsNullOrWhiteSpace(quote))
            metadata[DatedRecordExtractor<TRecord>.QuoteKey] = quote.Trim();
        else if (_options.Quote is not null)
            metadata.Remove(DatedRecordExtractor<TRecord>.QuoteKey);

        await _store.UpsertAsync([new KnowledgeDocument { Id = id, Text = _options.Text(record), Metadata = metadata }], ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Checks the quote of each record written for <paramref name="sourceId"/> (of this type, with
    /// <see cref="DatedRecordOptions{TRecord}.RecordType"/>) against <paramref name="document"/>, marking those whose
    /// quote is gone and clearing the mark on those whose quote is back. Only the metadata of records whose mark changes
    /// is updated, and only where the record is still as it was listed.
    /// </summary>
    /// <returns>0 and 0: it reads no records from the document.</returns>
    /// <exception cref="NotSupportedException">The store can't list (<see cref="IListableKnowledgeStore"/>).</exception>
    public async Task<(int Stored, int Refused)> ExtractAsync(
        string sourceId, ExtractedDocument document, IReadOnlyDictionary<string, string> header, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(header);
        if (_store is not IListableKnowledgeStore listable)
            throw new NotSupportedException($"The records' store ({_store.GetType().Name}) can't list.");

        var written = new KnowledgeFilter { ["source"] = sourceId, [WrittenKey] = "true" };
        if (_options.RecordType is { } recordType)
            written[DatedRecordExtractor<TRecord>.RecordTypeKey] = recordType;
        var chunks = await listable.ListAsync(written, int.MaxValue, ct: ct).ConfigureAwait(false);

        var sections = DatedRecordExtractor<TRecord>.Sections(document);
        foreach (var chunk in chunks)
        {
            if (!chunk.Metadata.TryGetValue(DatedRecordExtractor<TRecord>.QuoteKey, out var quote) || string.IsNullOrWhiteSpace(quote))
                continue;

            var gone = DatedRecordExtractor<TRecord>.Cite(quote, sections) is null;
            if (gone == chunk.Metadata.ContainsKey(PassageGoneKey))
                continue;

            var metadata = new Dictionary<string, string>(chunk.Metadata, StringComparer.Ordinal);
            if (gone)
                metadata[PassageGoneKey] = "true";
            else
                metadata.Remove(PassageGoneKey);
            await listable.UpdateMetadataAsync(chunk, metadata, ct).ConfigureAwait(false);
        }

        return (0, 0);
    }
}
