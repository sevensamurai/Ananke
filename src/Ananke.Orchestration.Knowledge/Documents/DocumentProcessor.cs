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
        return await ProcessAsync(stream, extension, uri.ToString(), description, enrichedTags, ct);
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

        // 3. Chunk
        var chunks = _chunker.Chunk(extracted);

        if (chunks.Count == 0)
            throw new InvalidOperationException(
                $"Chunking produced 0 chunks for '{sourceId}' " +
                $"({extracted.Sections.Count} sections were extracted). " +
                "Check the chunker configuration or document content.");

        _logger.LogDebug("Chunked '{SourceId}' into {ChunkCount} chunk(s)", sourceId, chunks.Count);

        // 4. Delete existing chunks for this source (clean re-index)
        await _store.DeleteAsync(new KnowledgeFilter { ["source"] = sourceId }, ct);

        // 5. Build documents with metadata
        var documents = chunks.Select((c, i) =>
        {
            var metadata = new Dictionary<string, string>(c.Metadata) { ["source"] = sourceId };

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
                Text = c.Text,
                Metadata = metadata
            };
        }).ToList();

        // 6. Upsert
        await _store.UpsertAsync(documents, ct);

        _logger.LogInformation(
            "Processed '{SourceId}': {SectionCount} sections, {ChunkCount} chunks stored",
            sourceId, extracted.Sections.Count, documents.Count);

        return new ProcessingResult(
            Sections: extracted.Sections.Count,
            Chunks: documents.Count,
            Source: sourceId,
            Description: description ?? string.Empty);
    }

    }
