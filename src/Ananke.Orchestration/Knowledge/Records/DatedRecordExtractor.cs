using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Knowledge.Documents;

namespace Ananke.Orchestration.Knowledge.Records;

/// <summary>What a <see cref="DatedRecordExtractor{TRecord}"/> asks for, and how it reads what comes back.</summary>
/// <typeparam name="TRecord">The product's record type; its JSON schema is what the model's reply is held to.</typeparam>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed record DatedRecordOptions<TRecord>
{
    /// <summary>The system prompt: what a record is, and what to leave out.</summary>
    public required string Instructions { get; init; }

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
/// is indexed again. Give it to <see cref="DocumentProcessor.Records"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each record is stored as <c>{source}:record:{n}</c> with the metadata <c>source</c>, the header's fields,
/// <see cref="DocumentHeaderOptions.EffectiveAtKey"/> (the record's own date, else the document's), <c>replaces</c>,
/// and the record itself as JSON under <see cref="RecordKey"/>, read back with <see cref="Read"/>.
/// </para>
/// <para>
/// A source's records are deleted before its document is read, so a document whose reply can't be read has no records
/// rather than stale ones, and <see cref="RecordExtractionException"/> is thrown. One attempt per document: a reply is
/// never fed back to be corrected. Wrap the model with <c>WithSpendingLimit</c> and <c>WithCallTimeout</c>.
/// </para>
/// </remarks>
/// <typeparam name="TRecord">The product's record type.</typeparam>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class DatedRecordExtractor<TRecord> : IDocumentRecords
{
    /// <summary>The metadata key a record's JSON is stored under.</summary>
    public const string RecordKey = "record";

    /// <summary>The metadata key for what a record replaces.</summary>
    public const string ReplacesKey = "replaces";

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
        _model = model;
        _store = store;
        _options = options;
    }

    /// <inheritdoc />
    /// <exception cref="RecordExtractionException">The model's reply isn't the schema's JSON.</exception>
    public async Task<int> ExtractAsync(
        string sourceId, ExtractedDocument document, IReadOnlyDictionary<string, string> header, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(header);

        await _store.DeleteAsync(new KnowledgeFilter { ["source"] = sourceId }, ct).ConfigureAwait(false);

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

        var kept = records.Where(r => r is not null && (_options.Keep?.Invoke(r) ?? true)).ToList();
        var documents = kept.Select((record, i) => new KnowledgeDocument
        {
            Id = $"{sourceId}:record:{i}",
            Text = _options.Text(record),
            Metadata = Metadata(sourceId, record, header),
        }).ToList();
        if (documents.Count > 0)
            await _store.UpsertAsync(documents, ct).ConfigureAwait(false);

        return documents.Count;
    }

    /// <summary>The record a stored chunk holds, or <see langword="default"/> when it holds none.</summary>
    public static TRecord? Read(KnowledgeChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        return chunk.Metadata.TryGetValue(RecordKey, out var json) ? JsonSerializer.Deserialize<TRecord>(json, Json) : default;
    }

    private Dictionary<string, string> Metadata(string sourceId, TRecord record, IReadOnlyDictionary<string, string> header)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["source"] = sourceId,
            [RecordKey] = JsonSerializer.Serialize(record, Json),
        };
        if (_options.Date?.Invoke(record) is { } dateText && DocumentHeaders.DateIn(dateText) is { } date)
            metadata[DocumentHeaderOptions.EffectiveAtKey] = date.ToString("O", CultureInfo.InvariantCulture);
        if (_options.Replaces?.Invoke(record) is { Length: > 0 } replaces)
            metadata[ReplacesKey] = replaces;
        if (_options.LinkPatterns is { Count: > 0 } patterns)
        {
            foreach (var (key, value) in DocumentLinks.Find(patterns, _options.Text(record)))
                metadata.TryAdd(key, value);
        }

        foreach (var (key, value) in _options.Fields?.Invoke(record) ?? new Dictionary<string, string>())
            metadata.TryAdd(key, value);
        foreach (var (key, value) in header)
            metadata.TryAdd(key, value);
        return metadata;
    }

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
