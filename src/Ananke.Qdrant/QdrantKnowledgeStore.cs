using Ananke.Orchestration.Knowledge;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Ananke.Qdrant;

/// <summary>
/// Qdrant-backed <see cref="IKnowledgeStore"/> for persistent, distributed vector search.
/// Automatically creates the collection on first use if it does not exist.
/// </summary>
/// <remarks>
/// <para>
/// Point IDs are deterministic UUIDs derived from the document ID string via
/// <see cref="Guid"/> namespace-based hashing, ensuring stable upsert/dedup behavior.
/// </para>
/// <para>
/// All document metadata is stored as Qdrant payload fields (keyword type).
/// The original text is stored in a <c>_text</c> payload field and returned with search results.
/// </para>
/// </remarks>
public sealed class QdrantKnowledgeStore : IKnowledgeStore
{
    private const string TextPayloadKey = "_text";

    // RFC 4122 §4.3 — predefined DNS namespace UUID used for deterministic v5 UUID generation
    private static readonly Guid UuidNamespaceDns = new("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

    private readonly QdrantClient _client;
    private readonly IEmbeddingModel _embedder;
    private readonly string _collectionName;
    private readonly uint _vectorSize;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    /// <summary>
    /// Creates a Qdrant-backed knowledge store.
    /// </summary>
    /// <param name="client">Qdrant gRPC client instance.</param>
    /// <param name="embedder">Embedding model for vectorizing documents and queries.</param>
    /// <param name="collectionName">Qdrant collection name. Default is <c>"knowledge"</c>.</param>
    /// <param name="vectorSize">
    /// Dimensionality of the embedding vectors. Must match the embedding model output.
    /// Default is <c>1536</c> (OpenAI text-embedding-3-small).
    /// </param>
    public QdrantKnowledgeStore(
        QdrantClient client,
        IEmbeddingModel embedder,
        string collectionName = "knowledge",
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
    public async Task<IReadOnlyList<KnowledgeChunk>> SearchAsync(
        string query, SearchOptions? options = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        await EnsureCollectionAsync(ct);

        options ??= new SearchOptions();
        var queryEmbedding = await _embedder.EmbedAsync(query, ct);
        var filter = BuildFilter(options.Filter);

        var results = await _client.SearchAsync(
            collectionName: _collectionName,
            vector: queryEmbedding,
            filter: filter,
            limit: (ulong)options.TopK,
            scoreThreshold: options.ScoreThreshold,
            payloadSelector: true,
            cancellationToken: ct);

        return results
            .Select(MapScoredPoint)
            .ToList();
    }

    /// <inheritdoc />
    public async Task UpsertAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        await EnsureCollectionAsync(ct);

        var docList = documents.ToList();
        if (docList.Count == 0) return;

        // Process in batches to prevent timeouts or hitting rate limits
        // 25 chunks per batch is a reasonable default for typical embedding APIs
        const int BatchSize = 25;

        for (var i = 0; i < docList.Count; i += BatchSize)
        {
            var batch = docList.Skip(i).Take(BatchSize).ToList();
            var texts = batch.Select(d => d.Text).ToList();
            var embeddings = await _embedder.EmbedBatchAsync(texts, ct);

            var points = new List<PointStruct>(batch.Count);
            for (var j = 0; j < batch.Count; j++)
            {
                var doc = batch[j];
                var payload = new Dictionary<string, Value>
                {
                    [TextPayloadKey] = doc.Text
                };

                foreach (var (key, value) in doc.Metadata)
                    payload[key] = value;

                points.Add(new PointStruct
                {
                    Id = ToPointId(doc.Id),
                    Vectors = embeddings[j].ToArray(),
                    Payload = { payload }
                });
            }

            await _client.UpsertAsync(_collectionName, points, cancellationToken: ct);
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(KnowledgeFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        await EnsureCollectionAsync(ct);

        var qdrantFilter = BuildFilter(filter);
        if (qdrantFilter is null) return;

        await _client.DeleteAsync(_collectionName, qdrantFilter, cancellationToken: ct);
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

                // Create payload index for source field to speed up deletion/filtering
                await _client.CreatePayloadIndexAsync(
                    _collectionName,
                    "source", // Assuming 'source' is the key used in metadata
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

    private static Filter? BuildFilter(KnowledgeFilter? filter)
    {
        if (filter is null or { Count: 0 })
            return null;

        var conditions = filter
            .Select(kv => Conditions.MatchKeyword(kv.Key, kv.Value))
            .ToList();

        return new Filter { Must = { conditions } };
    }

    private static KnowledgeChunk MapScoredPoint(ScoredPoint point)
    {
        var metadata = new Dictionary<string, string>();
        var text = string.Empty;

        foreach (var (key, value) in point.Payload)
        {
            var stringValue = value.StringValue;
            if (key == TextPayloadKey)
                text = stringValue;
            else
                metadata[key] = stringValue;
        }

        return new KnowledgeChunk
        {
            Id = point.Id.Uuid,
            Text = text,
            Score = point.Score,
            Metadata = metadata
        };
    }

    private static PointId ToPointId(string documentId) =>
        new() { Uuid = ToUuidV5(documentId).ToString("D") };


    /// <summary>
    /// RFC 4122 §4.3 — generates a version 5 UUID using SHA-1 hashing of the namespace UUID and name.
    /// </summary>
    /// <param name="name">The name from which to generate the UUID.</param>
    /// <returns>A version 5 UUID.</returns>
    private static Guid ToUuidV5(string name)
    {
        var namespaceId = UuidNamespaceDns;
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
