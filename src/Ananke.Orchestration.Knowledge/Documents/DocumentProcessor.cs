using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ananke.Orchestration.Knowledge.Documents;

/// <summary>
/// Orchestrates the full document ingestion pipeline: fetch → extract → chunk → store.
/// Acts as the universal entry point for indexing documents from any context —
/// agent tool calls, admin endpoints, batch scripts, or background jobs.
/// </summary>
public sealed class DocumentProcessor
{
    private readonly HttpClient _http;
    private readonly IReadOnlyList<IDocumentExtractor> _extractors;
    private readonly IDocumentChunker _chunker;
    private readonly IKnowledgeStore _store;
    private readonly long _maxContentLength;
    private readonly ILogger<DocumentProcessor> _logger;

    /// <summary>
    /// Creates a new document processor.
    /// </summary>
    /// <param name="http">
    /// HTTP client for fetching documents. Configure authentication (Bearer tokens, SAS tokens,
    /// signed URLs) on this client via <c>IHttpClientFactory</c>.
    /// </param>
    /// <param name="extractors">Available document extractors, selected by file extension.</param>
    /// <param name="chunker">Chunking strategy for splitting extracted text.</param>
    /// <param name="store">Knowledge store for persisting embedded chunks.</param>
    /// <param name="maxContentLength">Maximum document size in bytes. Default is 50 MB.</param>
    /// <param name="loggerFactory">Optional logger factory for diagnostic output.</param>
    public DocumentProcessor(
        HttpClient http,
        IReadOnlyList<IDocumentExtractor> extractors,
        IDocumentChunker chunker,
        IKnowledgeStore store,
        long maxContentLength = 50 * 1024 * 1024,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(extractors);
        ArgumentNullException.ThrowIfNull(chunker);
        ArgumentNullException.ThrowIfNull(store);

        _http = http;
        _extractors = extractors;
        _chunker = chunker;
        _store = store;
        _maxContentLength = maxContentLength;
        _logger = loggerFactory?.CreateLogger<DocumentProcessor>()
            ?? NullLogger<DocumentProcessor>.Instance;
    }

    /// <summary>
    /// How each document's header is read, if at all. When set, the fields it names go at the top of every chunk's text
    /// and into its metadata, and the date field becomes <see cref="DocumentHeaderOptions.EffectiveAtKey"/>.
    /// </summary>
    [Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
    public DocumentHeaderOptions? Headers { get; init; }

    /// <summary>
    /// A step run after a document's chunks are stored, that replaces its records with those read from it, if any.
    /// It is given the header's fields, so a record can take the document's date.
    /// </summary>
    [Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
    public IDocumentRecords? Records { get; init; }

    /// <summary>
    /// Patterns, by the kind of thing each finds, whose matches in a chunk's text are written into its metadata as links,
    /// <c>entity:{kind}:{match}</c>, with no model call: <c>card</c> → card keys such as <c>ZEN-6</c>, say. A pattern with
    /// a group named <c>key</c> links what the group matched. <c>DocumentStructureBuilder</c> turns them into
    /// <c>mentions</c> edges.
    /// </summary>
    [Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
    public IReadOnlyDictionary<string, System.Text.RegularExpressions.Regex>? LinkPatterns { get; init; }

    /// <summary>
    /// Section headings, compared without case, whose sections are left out of chunks and records: an
    /// <c>Authors' Addresses</c>, an attendee list, a template's checklist. The header is read before they are dropped.
    /// </summary>
    [Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
    public IReadOnlyList<string>? DropSections { get; init; }

    /// <summary>
    /// Applied to each section's text before chunking and records, after the header is read: page footers, form feeds.
    /// None by default.
    /// </summary>
    [Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
    public Func<string, string>? CleanText { get; init; }

    /// <summary>
    /// Fetches the document from <paramref name="uri"/>, extracts text, chunks, embeds,
    /// and stores it in the knowledge store. Existing chunks for the same source URI are
    /// replaced (delete + upsert) to ensure clean re-indexing.
    /// </summary>
    /// <param name="uri">The URL of the document to process.</param>
    /// <param name="description">
    /// A short description of the document's content/domain. Stored in the result and
    /// used downstream to build agent tool descriptions. Pass <see langword="null"/> and
    /// use <see cref="DocumentSummarizer.AutoDescribeAsync"/> afterwards for LLM-generated descriptions.
    /// </param>
    /// <param name="tags">Optional metadata tags to attach to all chunks from this document.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<ProcessingResult> ProcessAsync(
        Uri uri,
        string? description = null,
        IReadOnlyDictionary<string, string>? tags = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(uri);

        _logger.LogInformation("Processing document from URI '{Uri}'", uri);

        // 1. Fetch the document
        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;
        _logger.LogDebug("Fetched '{Uri}' — HTTP {StatusCode}, content-length: {ContentLength}",
            uri, (int)response.StatusCode, contentLength?.ToString() ?? "unknown");

        if (contentLength > _maxContentLength)
            throw new InvalidOperationException(
                $"Document at {uri} exceeds the maximum size of {_maxContentLength:N0} bytes " +
                $"(reported: {contentLength:N0} bytes).");

        var extension = Path.GetExtension(uri.AbsolutePath);
        if (string.IsNullOrEmpty(extension))
            throw new NotSupportedException(
                $"Cannot determine file extension from URI '{uri}'.");

        _logger.LogDebug("Resolved file extension '{Extension}' from URI '{Uri}'", extension, uri);

        // Inject the original URI for citation/transparency — distinct from sourceId (dedup key)
        Dictionary<string, string> enrichedTags = tags is not null
            ? new Dictionary<string, string>(tags)
            : [];
        enrichedTags.TryAdd("source_uri", uri.ToString());

        // 2. Extract, chunk, store
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        // 5.5: Wrap with a hard byte-cap so the limit applies regardless of whether
        // Content-Length was present or accurate in the response headers.
        await using var capped = new LimitedStream(stream, _maxContentLength, uri.ToString());
        return await ProcessAsync(capped, extension, uri.ToString(), description, enrichedTags, ct);
    }

    /// <summary>
    /// Processes a document from a stream — extracts text, chunks, embeds, and stores it.
    /// Use this overload for local files, in-memory buffers, or any non-HTTP source.
    /// Existing chunks for the same <paramref name="sourceId"/> are replaced.
    /// </summary>
    /// <param name="data">The document content stream.</param>
    /// <param name="fileExtension">The file extension including the leading dot (e.g. <c>".pdf"</c>, <c>".md"</c>).</param>
    /// <param name="sourceId">
    /// Identifier for this document source — used as the key for deduplication
    /// and as the <c>source</c> metadata value on stored chunks.
    /// Typically a file path, URI, or any stable unique identifier.
    /// </param>
    /// <param name="description">
    /// A short description of the document's content/domain. Stored in the result and
    /// used downstream to build agent tool descriptions. Pass <see langword="null"/> and
    /// use <see cref="DocumentSummarizer.AutoDescribeAsync"/> afterwards for LLM-generated descriptions.
    /// </param>
    /// <param name="tags">Optional metadata tags to attach to all chunks from this document.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<ProcessingResult> ProcessAsync(
        Stream data,
        string fileExtension,
        string sourceId,
        string? description = null,
        IReadOnlyDictionary<string, string>? tags = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileExtension);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        _logger.LogInformation("Processing '{SourceId}' (extension: '{FileExtension}')", sourceId, fileExtension);

        // 1. Find the right extractor
        var extractor = _extractors.FirstOrDefault(e => e.CanExtract(fileExtension))
            ?? throw new NotSupportedException(
                $"No document extractor registered for file extension '{fileExtension}'. " +
                $"Registered extractors: [{string.Join(", ", _extractors.Select(e => e.GetType().Name))}].");

        _logger.LogDebug("Selected extractor {ExtractorType} for '{SourceId}'",
            extractor.GetType().Name, sourceId);

        // 2. Extract
        var extracted = await extractor.ExtractAsync(data, ct);

        if (extracted.Sections.Count == 0)
            throw new InvalidOperationException(
                $"Extraction produced 0 sections for '{sourceId}' " +
                $"(extension: '{fileExtension}', extractor: {extractor.GetType().Name}). " +
                "The document may be empty or in an unsupported format.");

        _logger.LogDebug("Extracted {SectionCount} section(s) from '{SourceId}'",
            extracted.Sections.Count, sourceId);

        // 3. Read the header from the document as extracted, then shape what is chunked and handed to records
        var (prefix, headerFields, kept) = ReadHeader(extracted, tags);
        extracted = Shaped(extracted);

        // 4. Chunk
        var chunks = _chunker.Chunk(extracted);

        if (chunks.Count == 0)
            throw new InvalidOperationException(
                $"Chunking produced 0 chunks for '{sourceId}' " +
                $"({extracted.Sections.Count} sections were extracted). " +
                "Check the chunker configuration or document content.");

        _logger.LogDebug("Chunked '{SourceId}' into {ChunkCount} chunk(s)", sourceId, chunks.Count);

        // 5. Delete existing chunks for this source (clean re-index)
        await _store.DeleteAsync(new KnowledgeFilter { ["source"] = sourceId }, ct);

        // 6. Build documents with metadata, and the header's fields when the host asked for them
        var documents = chunks.Select((c, i) =>
        {
            var metadata = new Dictionary<string, string>(c.Metadata) { ["source"] = sourceId };
            foreach (var (key, value) in headerFields)
                metadata.TryAdd(key, value);
#pragma warning disable ANNKEEXP004 // LinkPatterns and the header keys are experimental; using them here is the processor's own business.
            if (kept.Count > 0)
                metadata[DocumentHeaderOptions.HeaderFieldsKey] = System.Text.Json.JsonSerializer.Serialize(kept);
            if (LinkPatterns is { Count: > 0 } patterns)
            {
                foreach (var (key, value) in DocumentLinks.Find(patterns, c.Text))
                    metadata.TryAdd(key, value);
            }
#pragma warning restore ANNKEEXP004

            if (description is { Length: > 0 })
                metadata["description"] = description;

            if (tags is not null)
            {
                foreach (var (key, value) in tags)
                    metadata.TryAdd(key, value);
            }

            return new KnowledgeDocument
            {
                Id = $"{sourceId}:chunk:{i}",
                Text = prefix is null ? c.Text : $"{prefix}\n{c.Text}",
                Metadata = metadata
            };
        }).ToList();

        // 7. Upsert
        await _store.UpsertAsync(documents, ct);

        // 8. Records, when the host asked for them: after the chunks, from the same document and header.
#pragma warning disable ANNKEEXP004 // Records is experimental; calling it here is the processor's own business.
        var records = Records is null ? 0 : await Records.ExtractAsync(sourceId, extracted, headerFields, ct);
#pragma warning restore ANNKEEXP004

        _logger.LogInformation(
            "Processed '{SourceId}': {SectionCount} sections, {ChunkCount} chunks stored",
            sourceId, extracted.Sections.Count, documents.Count);

        return new ProcessingResult(
            Sections: extracted.Sections.Count,
            Chunks: documents.Count,
            Source: sourceId,
            Description: description ?? string.Empty,
            Records: records);
    }

    /// <summary>
    /// The line that goes on top of each chunk (<c>[Title: … · Date: …]</c>), and the fields that go into its metadata
    /// with the date as <see cref="DocumentHeaderOptions.EffectiveAtKey"/>; nothing when <see cref="Headers"/> isn't set.
    /// </summary>
#pragma warning disable ANNKEEXP004 // Headers is experimental; reading it here is the processor's own business.
    private (string? Prefix, IReadOnlyDictionary<string, string> Fields, IReadOnlyList<string> Kept) ReadHeader(
        ExtractedDocument extracted, IReadOnlyDictionary<string, string>? tags)
    {
        if (Headers is not { } headers)
            return (null, new Dictionary<string, string>(), []);

        // What the document says, then what the caller knows about it (metadata kept beside it), compared without case.
        var read = new Dictionary<string, string>(headers.Read(extracted), StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in tags ?? new Dictionary<string, string>())
            read.TryAdd(key, value);

        var kept = headers.Fields.Where(read.ContainsKey).ToList();
        var fields = kept.ToDictionary(key => key, key => read[key], StringComparer.Ordinal);
        if (headers.DateField is { } dateField
            && read.TryGetValue(dateField, out var dateText)
            && (headers.ParseDate ?? DocumentHeaders.DateIn)(dateText) is { } date)
        {
            fields[DocumentHeaderOptions.EffectiveAtKey] = date.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        }

        if (headers.SupersededBy?.Invoke(read) is { Count: > 0 } supersededBy)
            fields[DocumentHeaderOptions.SupersededByKey] = string.Join(", ", supersededBy);

        if (headers.Replaces?.Invoke(read) is { Count: > 0 } replaces)
            fields[DocumentHeaderOptions.ReplacesSourcesKey] = System.Text.Json.JsonSerializer.Serialize(replaces);

        foreach (var updated in headers.Updates?.Invoke(read) ?? [])
            fields[$"{DocumentLinks.Prefix}{DocumentHeaderOptions.UpdatesKind}:{updated}"] = $"{DocumentHeaderOptions.UpdatesKind}:{updated}";

        var shown = kept.Select(key => $"{key}: {read[key]}").ToList();
        return (shown.Count == 0 ? null : $"[{string.Join(" · ", shown)}]", fields, kept);
    }

    /// <summary>The document with <see cref="DropSections"/> left out and <see cref="CleanText"/> applied.</summary>
    private ExtractedDocument Shaped(ExtractedDocument extracted)
    {
        if (DropSections is not { Count: > 0 } && CleanText is null)
            return extracted;

        var drop = new HashSet<string>(DropSections ?? [], StringComparer.OrdinalIgnoreCase);
        var sections = extracted.Sections
            .Where(s => s.SectionTitle is null || !drop.Contains(s.SectionTitle.Trim()))
            .Select(s => CleanText is null ? s : s with { Text = CleanText(s.Text) })
            .Where(s => !string.IsNullOrWhiteSpace(s.Text))
            .ToList();
        return extracted with { Sections = sections };
    }
#pragma warning restore ANNKEEXP004
}
