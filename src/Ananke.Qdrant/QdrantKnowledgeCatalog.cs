using Ananke.Orchestration.Knowledge;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Ananke.Qdrant;

/// <summary>
/// Qdrant-backed <see cref="IKnowledgeCatalog"/> storing one point per source document
/// in a dedicated catalog collection. The point vector is the embedding of the summary
/// and keywords, enabling semantic discovery via <see cref="DiscoverAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Point IDs are deterministic UUIDs derived from the source string, matching the
/// convention used by <see cref="QdrantKnowledgeStore"/>.
/// </para>
/// <para>
/// Catalog metadata is stored as Qdrant payload fields:
/// <c>source</c>, <c>keywords</c> (comma-separated), <c>category</c>,
/// <c>indexed_at</c> (ISO 8601), <c>chunk_count</c>, and optional <c>superseded_by</c>.
/// </para>
/// </remarks>
public sealed class QdrantKnowledgeCatalog : IKnowledgeCatalog
{
    private const string TextPayloadKey = "_text";
    private const string SourcePayloadKey = "source";
    private const string KeywordsPayloadKey = "keywords";
    private const string CategoryPayloadKey = "category";
    private const string IndexedAtPayloadKey = "indexed_at";
    private const string ChunkCountPayloadKey = "chunk_count";
    private const string SupersededByPayloadKey = "superseded_by";

    private readonly QdrantClient _client;
    private readonly IEmbeddingModel _embedder;
    private readonly string _collectionName;
    private readonly uint _vectorSize;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    /// <summary>
    /// Creates a Qdrant-backed knowledge catalog.
    /// </summary>
    /// <param name="client">Qdrant gRPC client instance.</param>
    /// <param name="embedder">Embedding model for vectorizing catalog summaries.</param>
    /// <param name="collectionName">
    /// Qdrant collection name for catalog entries. Default is <c>"knowledge_catalog"</c>.
    /// </param>
    /// <param name="vectorSize">
    /// Dimensionality of the embedding vectors. Must match the embedding model output.
    /// Default is <c>1536</c>.
    /// </param>
    public QdrantKnowledgeCatalog(
        QdrantClient client,
        IEmbeddingModel embedder,
        string collectionName = "knowledge_catalog",
        uint vectorSize = 1536)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(embedder);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        _client = client;
        _embedder = embedder;
        _collectionName = collectionName;
        _vectorSize = vectorSize;
    }

    /// <inheritdoc />
    public async Task IndexAsync(CatalogEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await EnsureCollectionAsync(ct);

        var embeddingText = BuildEmbeddingText(entry);
        var embedding = await _embedder.EmbedAsync(embeddingText, ct);

        var payload = new Dictionary<string, Value>
        {
            [TextPayloadKey] = embeddingText,
            [SourcePayloadKey] = entry.Source,
            [KeywordsPayloadKey] = string.Join(",", entry.Keywords),
            [CategoryPayloadKey] = entry.Category,
            [IndexedAtPayloadKey] = entry.IndexedAt.ToUnixTimeSeconds(),
            [ChunkCountPayloadKey] = entry.ChunkCount.ToString(),
            [SupersededByPayloadKey] = entry.SupersededBy is not null
                ? new Value { StringValue = entry.SupersededBy }
                : new Value { NullValue = default }
        };

        var point = new PointStruct
        {
            Id = ToPointId(entry.Source),
            Vectors = embedding.ToArray(),
            Payload = { payload }
        };

        await _client.UpsertAsync(_collectionName, [point], cancellationToken: ct);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string source, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        await EnsureCollectionAsync(ct);

        await _client.DeleteAsync(
            _collectionName,
            [ToPointId(source)],
            cancellationToken: ct);
    }

    /// <inheritdoc />
    public async Task<CatalogEntry?> GetAsync(string source, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        await EnsureCollectionAsync(ct);

        var points = await _client.RetrieveAsync(
            _collectionName,
            [ToPointId(source)],
            withPayload: true,
            cancellationToken: ct);

        var point = points.FirstOrDefault();
        return point is null ? null : MapRetrievedPoint(point);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CatalogSearchResult>> DiscoverAsync(
        string query, int topK = 5, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        await EnsureCollectionAsync(ct);

        var queryEmbedding = await _embedder.EmbedAsync(query, ct);

        // Filter out superseded entries (where 'superseded_by' is null)
        var filter = new Filter
        {
            Must = { Conditions.IsNull(SupersededByPayloadKey) }
        };

        var results = await _client.SearchAsync(
            collectionName: _collectionName,
            vector: queryEmbedding,
            filter: filter,
            limit: (ulong)topK,
            payloadSelector: true,
            cancellationToken: ct);

        return results
            .Select(MapScoredPoint)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CatalogEntry>> BrowseAsync(
        CatalogBrowseOptions? options = null, CancellationToken ct = default)
    {
        await EnsureCollectionAsync(ct);
        options ??= new CatalogBrowseOptions();

        var filter = new Filter();

        // Only show active entries
        filter.Must.Add(Conditions.IsNull(SupersededByPayloadKey));

        if (options.Category is not null)
        {
            filter.Must.Add(Conditions.MatchKeyword(CategoryPayloadKey, options.Category));
        }

        if (options.NotOlderThan is not null)
        {
            // ISO 8601 dates are lexicographically sortable, so string range works
            filter.Must.Add(Conditions.Range(
                IndexedAtPayloadKey,
                new global::Qdrant.Client.Grpc.Range { Gte = options.NotOlderThan.Value.ToUnixTimeSeconds() }));
        }

        // Qdrant Scroll API supports explicit ordering in recent versions (v1.7+)
        // via the 'order_by' parameter, but the .NET client method signature used here
        // might not expose it directly in this version.
        // We rely on indexed_at filtering to reduce the result set, but Scroll order
        // is implementation-defined (usually by ID).
        // To get true time-ordered browsing, we would need to use Recommendation or Search APIs
        // with a custom score, or client-side sorting if result set is small.
        // Given 'Limit' is applied at DB level, we might miss recent items if not sorted by time.
        // For now, we apply filters to narrow down the scope.

         var scrollResponse = await _client.ScrollAsync(
            _collectionName,
            filter: filter,
            limit: (uint)options.Limit,
            payloadSelector: true,
            cancellationToken: ct);

        return scrollResponse.Result
            .Select(MapRetrievedPoint)
            .OrderByDescending(e => e.IndexedAt)
            .ToList();
    }

    private async Task EnsureCollectionAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            var exists = await _client.CollectionExistsAsync(_collectionName, ct);
            if (!exists)
            {
                await _client.CreateCollectionAsync(
                    _collectionName,
                    new VectorParams { Size = _vectorSize, Distance = Distance.Cosine },
                    cancellationToken: ct);

                // Create payload indexes for efficient filtering
                await _client.CreatePayloadIndexAsync(
                    _collectionName,
                    SourcePayloadKey,
                    PayloadSchemaType.Keyword,
                    cancellationToken: ct);

                await _client.CreatePayloadIndexAsync(
                    _collectionName,
                    KeywordsPayloadKey,
                    PayloadSchemaType.Keyword,
                    cancellationToken: ct);

                await _client.CreatePayloadIndexAsync(
                    _collectionName,
                    CategoryPayloadKey,
                    PayloadSchemaType.Keyword,
                    cancellationToken: ct);

                await _client.CreatePayloadIndexAsync(
                    _collectionName,
                    IndexedAtPayloadKey,
                    PayloadSchemaType.Integer, // Unix timestamp range support
                    cancellationToken: ct);

                // SupersededBy needs fast IsNull check
                await _client.CreatePayloadIndexAsync(
                    _collectionName,
                    SupersededByPayloadKey,
                    PayloadSchemaType.Keyword,
                    cancellationToken: ct);
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static string BuildEmbeddingText(CatalogEntry entry)
    {
        var keywords = entry.Keywords.Count > 0
            ? string.Join(", ", entry.Keywords)
            : string.Empty;

        return $"{entry.Summary}\nKeywords: {keywords}\nCategory: {entry.Category}";
    }

    private static CatalogEntry MapPayload(IReadOnlyDictionary<string, Value> payload)
    {
        var source = payload.TryGetValue(SourcePayloadKey, out var s) ? s.StringValue : string.Empty;
        var keywords = payload.TryGetValue(KeywordsPayloadKey, out var kw)
            ? kw.StringValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList()
            : [];
        var category = payload.TryGetValue(CategoryPayloadKey, out var cat) ? cat.StringValue : string.Empty;

        var indexedAt = DateTimeOffset.MinValue;
        if (payload.TryGetValue(IndexedAtPayloadKey, out var ts))
        {
            if (ts.KindCase == Value.KindOneofCase.IntegerValue)
                indexedAt = DateTimeOffset.FromUnixTimeSeconds(ts.IntegerValue);
            else if (ts.KindCase == Value.KindOneofCase.DoubleValue)
                indexedAt = DateTimeOffset.FromUnixTimeSeconds((long)ts.DoubleValue);
            else if (ts.KindCase == Value.KindOneofCase.StringValue && DateTimeOffset.TryParse(ts.StringValue, out var parsed))
                indexedAt = parsed;
        }

        var chunkCount = payload.TryGetValue(ChunkCountPayloadKey, out var cc)
                         && int.TryParse(cc.StringValue, out var count)
            ? count
            : 0;
        var supersededBy = payload.TryGetValue(SupersededByPayloadKey, out var sb)
                           && sb.KindCase == Value.KindOneofCase.StringValue
                           && sb.StringValue.Length > 0
            ? sb.StringValue
            : null;

        // Summary is reconstructed from the embedded text or from the source payload
        var summary = payload.TryGetValue(TextPayloadKey, out var text)
            ? text.StringValue.Split('\n')[0]
            : string.Empty;

        return new CatalogEntry
        {
            Source = source,
            Summary = summary,
            Keywords = keywords,
            Category = category,
            IndexedAt = indexedAt,
            ChunkCount = chunkCount,
            SupersededBy = supersededBy
        };
    }

    private static CatalogEntry MapRetrievedPoint(RetrievedPoint point) =>
        MapPayload(point.Payload);

    private static CatalogSearchResult MapScoredPoint(ScoredPoint point) =>
        new()
        {
            Entry = MapPayload(point.Payload),
            Score = point.Score
        };

    private static PointId ToPointId(string source) =>
        new() { Uuid = ToUuidV5(source).ToString("D") };

    private static Guid ToUuidV5(string name)
    {
        var namespaceId = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8");
        var namespaceBytes = namespaceId.ToByteArray();
        SwapGuidBytes(namespaceBytes);

        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        var input = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, input, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, input, namespaceBytes.Length, nameBytes.Length);

        var hash = System.Security.Cryptography.SHA1.HashData(input);
        var result = new byte[16];
        Array.Copy(hash, result, 16);

        result[6] = (byte)((result[6] & 0x0F) | 0x50);
        result[8] = (byte)((result[8] & 0x3F) | 0x80);

        SwapGuidBytes(result);
        return new Guid(result);
    }

    private static void SwapGuidBytes(byte[] bytes)
    {
        (bytes[0], bytes[3]) = (bytes[3], bytes[0]);
        (bytes[1], bytes[2]) = (bytes[2], bytes[1]);
        (bytes[4], bytes[5]) = (bytes[5], bytes[4]);
        (bytes[6], bytes[7]) = (bytes[7], bytes[6]);
    }
}
