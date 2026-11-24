using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Knowledge.Documents;

namespace Ananke.Orchestration.Knowledge.Records;

/// <summary>
/// What a <see cref="DatedRecordExtractor{TRecord}"/> asks for, and how it reads what comes back; and how a
/// <see cref="DatedRecordWriter{TRecord}"/> stores the records given to it.
/// </summary>
/// <typeparam name="TRecord">The product's record type; its JSON schema is what the model's reply is held to.</typeparam>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed record DatedRecordOptions<TRecord>
{
    /// <summary>
    /// The system prompt: what a record is, and what to leave out. A <see cref="DatedRecordExtractor{TRecord}"/> needs
    /// it; a <see cref="DatedRecordWriter{TRecord}"/> makes no model call and doesn't read it.
    /// </summary>
    public string Instructions { get; init; } = "";

    /// <summary>A record's text, as it is embedded and searched.</summary>
    public required Func<TRecord, string> Text { get; init; }

    /// <summary>A record's own date, if it has one; its first <c>yyyy-MM-dd</c> is taken. Otherwise the document's.</summary>
    public Func<TRecord, string?>? Date { get; init; }

    /// <summary>What a record says it replaces, if anything, kept as <c>replaces</c> metadata.</summary>
    public Func<TRecord, string?>? Replaces { get; init; }

    /// <summary>
    /// A record's properties to keep in its metadata (its status, its subject), so records can be listed and filtered
    /// by them (<see cref="DatedRecordReader{TRecord}.ListAsync"/>). A property here wins over a header field of the
    /// same name.
    /// </summary>
    public Func<TRecord, IReadOnlyDictionary<string, string>>? Fields { get; init; }

    /// <summary>
    /// A record's quoted passage, if the records must cite one. With it set, a record is stored only when its quote is
    /// found in one section of the document's text as the records step receives it, ignoring case and runs of
    /// whitespace; a record with no quote, or a blank one, isn't stored. A stored record keeps its quote as
    /// <see cref="DatedRecordExtractor{TRecord}.QuoteKey"/> metadata, and the title of the section it was found in, if
    /// that section has one, as <see cref="DatedRecordExtractor{TRecord}.SectionKey"/>, in place of any value
    /// <see cref="Fields"/> or the header gives those keys. Nothing is matched by similarity.
    /// </summary>
    public Func<TRecord, string?>? Quote { get; init; }

    /// <summary>
    /// The name of this extractor's record type (<c>decision</c>, <c>question</c>), for a store that keeps records of
    /// several types, each from its own extractor. With it set, each record is stored as
    /// <c>{source}:record:{type}:{n}</c> with the name as <see cref="DatedRecordExtractor{TRecord}.RecordTypeKey"/>
    /// metadata, and a document indexed again has only this type's records replaced; a
    /// <see cref="DatedRecordReader{TRecord}"/> given the same name reads only them. Without it, an extractor replaces
    /// every extracted record of the source in its store, so each extractor that shares a store needs a name of its own.
    /// Records an extractor stored before it was given a name aren't deleted by it: delete them by their <c>source</c>
    /// before indexing again.
    /// </summary>
    public string? RecordType { get; init; }

    /// <summary>The product's own check on a record, such as naming no person; a record it refuses isn't stored.</summary>
    public Func<TRecord, bool>? Keep { get; init; }

    /// <summary>
    /// Patterns, by kind, whose matches in a record's text are written into its metadata as links,
    /// <c>entity:{kind}:{match}</c>, as
    /// <see cref="DocumentProcessor.LinkPatterns"/> does for chunks.
    /// </summary>
    public IReadOnlyDictionary<string, System.Text.RegularExpressions.Regex>? LinkPatterns { get; init; }

    /// <summary>The reply's output limit. Default is 8,000 tokens.</summary>
    public int MaxOutputTokens { get; init; } = 8000;

    /// <summary>How much of a document's text is sent; the rest is cut. Default is 200,000 characters.</summary>
    public int MaxInputCharacters { get; init; } = 200_000;
}

/// <summary>
/// Reads typed, dated records from each document with one model call, held to the JSON schema of
/// <c>{ "records": [ TRecord ] }</c>, and keeps them in an <see cref="IKnowledgeStore"/> of their own: embedded by
/// <see cref="DatedRecordOptions{TRecord}.Text"/>, searchable beside the passages, and replaced whenever their document
/// is indexed again. Give it to <see cref="DocumentProcessor.Records"/>. Records a
/// <see cref="DatedRecordWriter{TRecord}"/> stored in the same store are left alone.
/// </summary>
/// <remarks>
/// <para>
/// Each record is stored as <c>{source}:record:{n}</c> (<c>{source}:record:{type}:{n}</c> with
/// <see cref="DatedRecordOptions{TRecord}.RecordType"/>) with the metadata <c>source</c>, the header's fields,
/// <see cref="DocumentHeaderOptions.EffectiveAtKey"/> (the record's own date, else the document's), <c>replaces</c>,
/// <see cref="ExtractedKey"/>, and the record itself as JSON under <see cref="RecordKey"/>, read back with
/// <see cref="Read"/>; with
/// <see cref="DatedRecordOptions{TRecord}.Quote"/>, also its quote under <see cref="QuoteKey"/> and its section's title
/// under <see cref="SectionKey"/>; with <see cref="DatedRecordOptions{TRecord}.RecordType"/>, the type's name under
/// <see cref="RecordTypeKey"/>.
/// </para>
/// <para>
/// A source's extracted records (of this type, with <see cref="DatedRecordOptions{TRecord}.RecordType"/>) are deleted
/// before its document is read, so a document whose reply can't be read has no records
/// rather than stale ones, and <see cref="RecordExtractionException"/> is thrown. One attempt per document: a reply is
/// never fed back to be corrected. Wrap the model with <c>WithSpendingLimit</c> and <c>WithCallTimeout</c>.
/// </para>
/// </remarks>
/// <typeparam name="TRecord">The product's record type.</typeparam>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed partial class DatedRecordExtractor<TRecord> : IDocumentRecords
{
    /// <summary>The metadata key a record's JSON is stored under.</summary>
    public const string RecordKey = "record";

    /// <summary>The metadata key for what a record replaces.</summary>
    public const string ReplacesKey = "replaces";

    /// <summary>The metadata key for a record's quoted passage (<see cref="DatedRecordOptions{TRecord}.Quote"/>).</summary>
    public const string QuoteKey = "quote";

    /// <summary>The metadata key for the title of the section a record's quote was found in, as chunks carry theirs.</summary>
    public const string SectionKey = "section";

    /// <summary>The metadata key for a record's type (<see cref="DatedRecordOptions{TRecord}.RecordType"/>).</summary>
    public const string RecordTypeKey = "record_type";

    /// <summary>
    /// The metadata key an extracted record carries, with the value <c>true</c>: what an extractor deletes when its
    /// document is indexed again, leaving records a <see cref="DatedRecordWriter{TRecord}"/> stored.
    /// </summary>
    public const string ExtractedKey = "extracted";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly AgentResponseFormat Format =
        new($"{typeof(TRecord).Name.ToLowerInvariant()}_records", JsonSchemaGenerator.Generate<Reply>());

    private readonly IAgentModel _model;
    private readonly IKnowledgeStore _store;
    private readonly DatedRecordOptions<TRecord> _options;

    /// <summary>Reads records with <paramref name="model"/> and keeps them in <paramref name="store"/>.</summary>
    public DatedRecordExtractor(IAgentModel model, IKnowledgeStore store, DatedRecordOptions<TRecord> options)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Instructions);
        if (options.RecordType is { } recordType)
            ArgumentException.ThrowIfNullOrWhiteSpace(recordType, nameof(options));
        _model = model;
        _store = store;
        _options = options;
        Owns = [(store, options.RecordType)];
        WritesTo = [store];
    }

    /// <inheritdoc />
    /// <remarks>Its store, with its <see cref="DatedRecordOptions{TRecord}.RecordType"/>.</remarks>
    public IReadOnlyList<(IKnowledgeStore Store, string? RecordType)> Owns { get; }

    /// <inheritdoc />
    /// <remarks>Its store.</remarks>
    public IReadOnlyList<IKnowledgeStore> WritesTo { get; }

    /// <inheritdoc />
    /// <returns>
    /// How many records were stored, and how many the reply held that weren't: refused by
    /// <see cref="DatedRecordOptions{TRecord}.Keep"/>, or whose <see cref="DatedRecordOptions{TRecord}.Quote"/> was found
    /// in no section.
    /// </returns>
    /// <exception cref="RecordExtractionException">The model's reply isn't the schema's JSON.</exception>
    public async Task<(int Stored, int Refused)> ExtractAsync(
        string sourceId, ExtractedDocument document, IReadOnlyDictionary<string, string> header, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(header);

        var own = new KnowledgeFilter { ["source"] = sourceId, [ExtractedKey] = "true" };
        if (_options.RecordType is { } recordType)
            own[RecordTypeKey] = recordType;
        await _store.DeleteAsync(own, ct).ConfigureAwait(false);

        var response = await _model.GenerateAsync(new AgentRequest
        {
            SystemPrompt = _options.Instructions,
            Messages = [AgentMessage.User(Prompt(sourceId, document, header))],
            ResponseFormat = Format,
            MaxOutputTokens = _options.MaxOutputTokens,
        }, ct).ConfigureAwait(false);

        IReadOnlyList<TRecord> records;
        try
        {
            records = JsonSerializer.Deserialize<Reply>(JsonReply.Extract(response.Text ?? ""), Json)?.Records
                ?? throw new JsonException("The reply had no records.");
        }
        catch (JsonException e)
        {
            throw new RecordExtractionException(
                $"The records for '{sourceId}' couldn't be read from the model's reply: {e.Message}", e);
        }

        var read = records.Where(r => r is not null).ToList();
        var kept = read.Where(r => _options.Keep?.Invoke(r) ?? true).ToList();
        var sections = _options.Quote is null ? [] : Sections(document);
        var idPrefix = _options.RecordType is { } type ? $"{sourceId}:record:{type}:" : $"{sourceId}:record:";
        var documents = new List<KnowledgeDocument>(kept.Count);
        foreach (var record in kept)
        {
            var citation = _options.Quote is { } quoteOf ? Cite(quoteOf(record), sections) : null;
            if (_options.Quote is not null && citation is null)
                continue;

            var metadata = Metadata(_options, sourceId, record, header);
            metadata[ExtractedKey] = "true";
            if (citation is { } cited)
            {
                metadata[QuoteKey] = cited.Quote;
                if (cited.Section is { } section)
                    metadata[SectionKey] = section;
                else
                    metadata.Remove(SectionKey);
            }

            documents.Add(new KnowledgeDocument
            {
                Id = $"{idPrefix}{documents.Count}",
                Text = _options.Text(record),
                Metadata = metadata,
            });
        }
        if (documents.Count > 0)
            await _store.UpsertAsync(documents, ct).ConfigureAwait(false);

        return (documents.Count, read.Count - documents.Count);
    }

    /// <summary>The record a stored chunk holds, or <see langword="default"/> when it holds none.</summary>
    public static TRecord? Read(KnowledgeChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        return chunk.Metadata.TryGetValue(RecordKey, out var json) ? JsonSerializer.Deserialize<TRecord>(json, Json) : default;
    }

    /// <summary>A record's metadata as <paramref name="options"/> say, without what only an extractor or a writer adds.</summary>
    internal static Dictionary<string, string> Metadata(
        DatedRecordOptions<TRecord> options, string sourceId, TRecord record, IReadOnlyDictionary<string, string> header)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["source"] = sourceId,
            [RecordKey] = JsonSerializer.Serialize(record, Json),
        };
        if (options.Date?.Invoke(record) is { } dateText && DocumentHeaders.DateIn(dateText) is { } date)
            metadata[DocumentHeaderOptions.EffectiveAtKey] = date.ToString("O", CultureInfo.InvariantCulture);
        if (options.Replaces?.Invoke(record) is { Length: > 0 } replaces)
            metadata[ReplacesKey] = replaces;
        if (options.LinkPatterns is { Count: > 0 } patterns)
        {
            foreach (var (key, value) in DocumentLinks.Find(patterns, options.Text(record)))
                metadata.TryAdd(key, value);
        }

        foreach (var (key, value) in options.Fields?.Invoke(record) ?? new Dictionary<string, string>())
            metadata.TryAdd(key, value);
        foreach (var (key, value) in header)
            metadata.TryAdd(key, value);
        if (options.RecordType is { } recordType)
            metadata[RecordTypeKey] = recordType;

        // Whether a record was extracted or written, and its mark, are set by the extractor or the writer alone.
        metadata.Remove(ExtractedKey);
        metadata.Remove(DatedRecordWriter<TRecord>.WrittenKey);
        metadata.Remove(DatedRecordWriter<TRecord>.PassageGoneKey);
        return metadata;
    }

    /// <summary>
    /// <paramref name="quote"/>, trimmed, and the title of the first of <paramref name="sections"/> it is in, ignoring
    /// case and runs of whitespace; <see langword="null"/> when it is blank or in none of them.
    /// </summary>
    internal static (string Quote, string? Section)? Cite(string? quote, List<(string? Title, string Text)> sections)
    {
        if (string.IsNullOrWhiteSpace(quote))
            return null;

        var trimmed = quote.Trim();
        var wanted = Whitespace().Replace(trimmed, " ");
        var index = sections.FindIndex(s => s.Text.Contains(wanted, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? null : (trimmed, sections[index].Title is { Length: > 0 } title ? title : null);
    }

    /// <summary>Each section's title, and its text with its runs of whitespace made one space, for quotes to be found in.</summary>
    internal static List<(string? Title, string Text)> Sections(ExtractedDocument document) =>
        [.. document.Sections.Select(s => (s.SectionTitle, Whitespace().Replace(s.Text, " ")))];

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private string Prompt(string sourceId, ExtractedDocument document, IReadOnlyDictionary<string, string> header)
    {
        var text = new StringBuilder();
        text.Append("Document: ").AppendLine(sourceId);
        foreach (var (key, value) in header.Where(h => h.Key != DocumentHeaderOptions.EffectiveAtKey))
            text.Append(key).Append(": ").AppendLine(value);
        text.AppendLine();

        var body = string.Join("\n\n", document.Sections.Select(s => s.SectionTitle is { } title ? $"## {title}\n{s.Text}" : s.Text));
        text.Append(body.Length > _options.MaxInputCharacters ? body[.._options.MaxInputCharacters] : body);
        return text.ToString();
    }

    /// <summary>The reply's shape: the schema the model is held to.</summary>
    private sealed record Reply([property: JsonPropertyName("records")] IReadOnlyList<TRecord> Records);
}

/// <summary>A document's records couldn't be read from the model's reply; the source is left with none.</summary>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class RecordExtractionException(string message, Exception? inner = null) : Exception(message, inner);
