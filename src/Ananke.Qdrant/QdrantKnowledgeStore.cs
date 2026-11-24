using System.Globalization;
using Ananke.Orchestration.Knowledge;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge.Embeddings;
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
/// The document ID is stored in an <c>_id</c> payload field and returned as the chunk's ID; a point written without
/// one is returned with its point ID. The key <c>id</c> in a filter matches the chunk's ID through its point ID, not a
/// metadata key.
/// </para>
/// </remarks>
#pragma warning disable ANNKEEXP004 // implements the experimental listing
public sealed class QdrantKnowledgeStore : IListableKnowledgeStore
#pragma warning restore ANNKEEXP004
{
    private const string TextPayloadKey = "_text";
    private const string IdPayloadKey = "_id";

    /// <summary>
    /// The filter key read as the chunk's ID: matched on the point ID derived from it, which Qdrant looks up directly,
    /// rather than on <c>_id</c>, which has no payload index.
    /// </summary>
    private const string IdFilterKey = "id";

    // RFC 4122 §4.3 — predefined DNS namespace UUID used for deterministic v5 UUID generation
    private static readonly Guid UuidNamespaceDns = new("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

    private readonly QdrantClient _client;
    private readonly IEmbeddingModel _embedder;
    private readonly string _collectionName;
    private readonly uint _vectorSize;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _dateIndexes = new(StringComparer.Ordinal);
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
        options ??= new SearchOptions();
        if (options.Mode != SearchMode.Dense)
        {
            throw new NotSupportedException(
                $"QdrantKnowledgeStore searches by embedding only; SearchMode.{options.Mode} is not supported.");
        }

        await EnsureCollectionAsync(ct).ConfigureAwait(false);
        var queryEmbedding = await _embedder.EmbedAsync(query, ct).ConfigureAwait(false);
        var filter = BuildFilter(options.Filter);

        var results = await _client.QueryAsync(
            collectionName: _collectionName,
            query: queryEmbedding.ToArray(),
            filter: filter,
#pragma warning disable ANNKEEXP004 // limits per source, an experimental option
            limit: (ulong)options.Candidates(),
#pragma warning restore ANNKEEXP004
            scoreThreshold: options.ScoreThreshold,
            payloadSelector: true,
            cancellationToken: ct).ConfigureAwait(false);

#pragma warning disable ANNKEEXP004 // limits per source, an experimental option
        return results
            .Select(MapScoredPoint)
            .LimitPerSource(options);
#pragma warning restore ANNKEEXP004
    }

    /// <inheritdoc />
    public async Task UpsertAsync(IEnumerable<KnowledgeDocument> documents, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        await EnsureCollectionAsync(ct).ConfigureAwait(false);

        var docList = documents.ToList();
        if (docList.Count == 0) return;

        // Process in batches to prevent timeouts or hitting rate limits
        // 25 chunks per batch is a reasonable default for typical embedding APIs
        const int BatchSize = 25;

        for (var i = 0; i < docList.Count; i += BatchSize)
        {
            var batch = docList.Skip(i).Take(BatchSize).ToList();
            var texts = batch.Select(d => d.Text).ToList();
            var embeddings = await _embedder.EmbedBatchAsync(texts, ct).ConfigureAwait(false);

            var points = new List<PointStruct>(batch.Count);
            for (var j = 0; j < batch.Count; j++)
            {
                var doc = batch[j];
                var payload = new Dictionary<string, Value>();
                foreach (var (key, value) in doc.Metadata)
                    payload[key] = value;

                // Set after the metadata, so a metadata key of the same name cannot replace the text or the id.
                payload[TextPayloadKey] = doc.Text;
                payload[IdPayloadKey] = doc.Id;

                points.Add(new PointStruct
                {
                    Id = ToPointId(doc.Id),
                    Vectors = embeddings[j].ToArray(),
                    Payload = { payload }
                });
            }

            await _client.UpsertAsync(_collectionName, points, cancellationToken: ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(KnowledgeFilter filter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
#pragma warning disable ANNKEEXP004 // reads the experimental AnyOf
        if (filter.Count == 0 && filter.AnyOf.Count == 0)
            throw new ArgumentException("A delete with an empty filter would remove every document; name what to delete.", nameof(filter));
#pragma warning restore ANNKEEXP004
        await EnsureCollectionAsync(ct).ConfigureAwait(false);

        await _client.DeleteAsync(_collectionName, BuildFilter(filter)!, cancellationToken: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Without <paramref name="order"/>, Qdrant scrolls by point id, a hash of the chunk's id. With it, the store makes a
    /// datetime payload index on its key the first time it is asked, scrolls the dated points by it, newest first, and,
    /// when there is room left and no lower bound, adds the points without a date (or whose value isn't one) by point
    /// id.
    /// </remarks>
#pragma warning disable ANNKEEXP004 // implements the experimental listing
    public async Task<IReadOnlyList<KnowledgeChunk>> ListAsync(
        KnowledgeFilter filter, int limit = 1000, KnowledgeDateOrder? order = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        await EnsureCollectionAsync(ct).ConfigureAwait(false);
        if (limit == 0)
            return [];
        if (order is null)
            return await ScrollAsync(BuildFilter(filter), limit, ct).ConfigureAwait(false);

        ArgumentException.ThrowIfNullOrWhiteSpace(order.Key, nameof(order));
        await EnsureDateIndexAsync(order.Key, ct).ConfigureAwait(false);

        var listed = await ScrollByDateAsync(filter, order, limit, ct).ConfigureAwait(false);
        if (listed.Count >= limit || order.Since is not null)
            return listed;

        var undated = BuildFilter(filter) ?? new Filter();
        undated.MustNot.Add(Conditions.DatetimeRange(PayloadPath(order.Key), gte: DateTimeOffset.MinValue.UtcDateTime));
        listed.AddRange(await ScrollAsync(undated, limit - listed.Count, ct).ConfigureAwait(false));
        return listed;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The keys whose value changes are set, then the keys that go are removed, each by a filter on the point id, the
    /// listed text and the metadata the point should have at that step. A point upserted or deleted in between matches
    /// neither, so the write is kept; a key added since the listing is kept too. Metadata keys <c>_text</c> and
    /// <c>_id</c> are left out, as in <see cref="UpsertAsync"/>.
    /// </remarks>
    public async Task UpdateMetadataAsync(KnowledgeChunk listed, IReadOnlyDictionary<string, string> metadata, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(listed);
        ArgumentNullException.ThrowIfNull(metadata);
        await EnsureCollectionAsync(ct).ConfigureAwait(false);

        static bool Kept(string key) => key is not (TextPayloadKey or IdPayloadKey);
        var changed = metadata
            .Where(kv => Kept(kv.Key) && !(listed.Metadata.TryGetValue(kv.Key, out var was) && was == kv.Value))
            .ToDictionary(kv => kv.Key, kv => (Value)kv.Value);
        var removed = listed.Metadata.Keys.Where(key => Kept(key) && !metadata.ContainsKey(key)).ToList();

        var expected = listed.Metadata.Where(kv => Kept(kv.Key)).ToDictionary();
        if (changed.Count > 0)
        {
            await _client.SetPayloadAsync(_collectionName, changed, Unchanged(listed, expected), cancellationToken: ct).ConfigureAwait(false);
            foreach (var (key, value) in changed)
                expected[key] = value.StringValue;
        }

        if (removed.Count > 0)
        {
            await _client.DeletePayloadAsync(_collectionName, [.. removed.Select(PayloadPath)], Unchanged(listed, expected), cancellationToken: ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The point of <paramref name="listed"/>, while it has the listed text and <paramref name="metadata"/>. A key
    /// holding <c>"</c> or <c>\</c>, which Qdrant refuses in a filter, is not matched, so it doesn't stop the update.
    /// </summary>
    private static Filter Unchanged(KnowledgeChunk listed, IReadOnlyDictionary<string, string> metadata) => new()
    {
        Must =
        {
            Conditions.HasId(ToUuidV5(listed.Id)),
            Conditions.MatchKeyword(TextPayloadKey, listed.Text),
            metadata.Where(kv => !kv.Key.Contains('"') && !kv.Key.Contains('\\'))
                .Select(kv => Conditions.MatchKeyword(PayloadPath(kv.Key), kv.Value)),
        },
    };

    /// <summary>
    /// The dated points matching <paramref name="filter"/>, newest first by <paramref name="order"/>'s key, at most
    /// <paramref name="limit"/>, 256 to a page. Ordered scrolling has no page offset, so each page starts at the oldest
    /// date the one before reached and leaves out the points already listed at that date.
    /// </summary>
    private async Task<List<KnowledgeChunk>> ScrollByDateAsync(
        KnowledgeFilter filter, KnowledgeDateOrder order, int limit, CancellationToken ct)
    {
        var since = (order.Since ?? DateTimeOffset.MinValue).UtcDateTime;
        var listed = new List<KnowledgeChunk>();
        var atOldest = new List<Guid>();
        DateTime? oldest = null;
        while (listed.Count < limit)
        {
            var dated = BuildFilter(filter) ?? new Filter();
            dated.Must.Add(Conditions.DatetimeRange(PayloadPath(order.Key), gte: since, lte: oldest));
            if (atOldest.Count > 0)
                dated.MustNot.Add(Conditions.HasId(atOldest));

            var size = Math.Min(256, limit - listed.Count);
            var page = await _client.ScrollAsync(
                _collectionName,
                filter: dated,
                limit: (uint)size,
                payloadSelector: true,
                orderBy: new OrderBy { Key = PayloadPath(order.Key), Direction = Direction.Desc },
                cancellationToken: ct).ConfigureAwait(false);

            foreach (var point in page.Result)
            {
                listed.Add(MapPayload(point.Id, point.Payload, 0f));

                // A value Qdrant reads as a date and .NET doesn't leaves no date to start the next page from.
                if (DateIn(point.Payload, order.Key) is not { } date)
                    return listed;
                if (date != oldest)
                {
                    oldest = date;
                    atOldest.Clear();
                }

                atOldest.Add(Guid.Parse(point.Id.Uuid));
            }

            if (page.Result.Count < size)
                break;
        }

        return listed;
    }

    /// <summary>The date under <paramref name="key"/> in UTC, to the microsecond as Qdrant holds it; none when it isn't one.</summary>
    private static DateTime? DateIn(IDictionary<string, Value> payload, string key) =>
        payload.TryGetValue(key, out var value)
        && DateTimeOffset.TryParse(value.StringValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? new DateTime(date.UtcTicks - (date.UtcTicks % 10), DateTimeKind.Utc)
            : null;
#pragma warning restore ANNKEEXP004

    /// <summary>The points matching <paramref name="filter"/>, by point id, at most <paramref name="limit"/>.</summary>
    private async Task<List<KnowledgeChunk>> ScrollAsync(Filter? filter, int limit, CancellationToken ct)
    {
        var listed = new List<KnowledgeChunk>();
        PointId? offset = null;
        while (listed.Count < limit)
        {
            var page = await _client.ScrollAsync(
                _collectionName,
                filter: filter,
                limit: (uint)Math.Min(256, limit - listed.Count),
                offset: offset,
                payloadSelector: true,
                cancellationToken: ct).ConfigureAwait(false);
            listed.AddRange(page.Result.Select(p => MapPayload(p.Id, p.Payload, 0f)));
            if (page.NextPageOffset is null || page.Result.Count == 0)
                break;
            offset = page.NextPageOffset;
        }

        return listed;
    }

    /// <summary>Makes a datetime payload index on <paramref name="key"/>, once per store; ordering by a key needs one.</summary>
    private async Task EnsureDateIndexAsync(string key, CancellationToken ct)
    {
        if (_dateIndexes.ContainsKey(key))
            return;

        await _client.CreatePayloadIndexAsync(_collectionName, PayloadPath(key), PayloadSchemaType.Datetime, wait: true, cancellationToken: ct)
            .ConfigureAwait(false);
        _dateIndexes.TryAdd(key, true);
    }

    private async Task EnsureCollectionAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized) return;

            var exists = await _client.CollectionExistsAsync(_collectionName, ct).ConfigureAwait(false);
            if (!exists)
            {
                await _client.CreateCollectionAsync(
                    _collectionName,
                    new VectorParams { Size = _vectorSize, Distance = Distance.Cosine },
                    cancellationToken: ct).ConfigureAwait(false);

                // Create payload index for source field to speed up deletion/filtering
                await _client.CreatePayloadIndexAsync(
                    _collectionName,
                    "source", // Assuming 'source' is the key used in metadata
                    PayloadSchemaType.Keyword,
                    cancellationToken: ct).ConfigureAwait(false);
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
        if (filter is null)
            return null;

#pragma warning disable ANNKEEXP004 // applies the experimental AnyOf
        List<Condition> conditions =
        [
            .. filter.Select(kv => kv.Key == IdFilterKey
                ? Conditions.HasId(ToUuidV5(kv.Value))
                : Conditions.MatchKeyword(PayloadPath(kv.Key), kv.Value)),
            .. filter.AnyOf.Select(kv => kv.Key == IdFilterKey
                ? Conditions.HasId([.. kv.Value.Select(ToUuidV5)])
                : Conditions.Match(PayloadPath(kv.Key), kv.Value)),
        ];
#pragma warning restore ANNKEEXP004

        return conditions.Count == 0 ? null : new Filter { Must = { conditions } };
    }

    /// <summary>
    /// The path Qdrant reads as the top-level payload field <paramref name="key"/>, for a filter, an order or an index.
    /// A key is a path in which <c>.</c> steps into a nested field and only letters, digits, <c>_</c> and <c>-</c> may
    /// go unquoted, so a key holding any other character (<c>entity:document:notes/a.md</c>) is put in double quotes.
    /// Qdrant has no escape inside the quotes, so it refuses a key holding <c>"</c> or <c>\</c>.
    /// </summary>
    private static string PayloadPath(string key) =>
        key.All(c => char.IsLetterOrDigit(c) || c is '_' or '-') ? key : $"\"{key}\"";

    private static KnowledgeChunk MapScoredPoint(ScoredPoint point) => MapPayload(point.Id, point.Payload, point.Score);

    private static KnowledgeChunk MapPayload(PointId id, IDictionary<string, Value> payload, float score)
    {
        var metadata = new Dictionary<string, string>();
        var text = string.Empty;
        var documentId = id.Uuid;

        foreach (var (key, value) in payload)
        {
            var stringValue = value.StringValue;
            if (key == TextPayloadKey)
                text = stringValue;
            else if (key == IdPayloadKey)
                documentId = stringValue;
            else
                metadata[key] = stringValue;
        }

        return new KnowledgeChunk
        {
            Id = documentId,
            Text = text,
            Score = score,
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
