using System.Security.Cryptography;
using System.Text;
using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Tools;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Ananke.Qdrant;

/// <summary>
/// Qdrant-backed <see cref="IToolMemory"/> that stores tool entries as dense vectors
/// and performs kNN recall for the semantic tool gate (Phase 2).
/// </summary>
/// <remarks>
/// <para>
/// Each tool is stored as a Qdrant point whose vector is the embedding of its
/// concatenated description and tags. On <see cref="RecallAsync"/>, the query text
/// is embedded and the top-<c>k</c> nearest neighbours are returned, filtered by
/// <see cref="ToolHealth"/> and optional tag constraints.
/// </para>
/// <para>
/// Payload keys stored per point: <c>kit_name</c>, <c>tool_name</c>,
/// <c>description</c>, <c>tags</c>, <c>health</c>, <c>hit_count</c>, <c>last_used</c>.
/// </para>
/// <para>
/// The collection is created automatically on first use.
/// </para>
/// </remarks>
public sealed class QdrantToolMemory : IToolMemory
{
    private const string KitNameKey = "kit_name";
    private const string ToolNameKey = "tool_name";
    private const string DescriptionKey = "description";
    private const string TagsKey = "tags";
    private const string HealthKey = "health";
    private const string HitCountKey = "hit_count";
    private const string LastUsedKey = "last_used";

    private readonly QdrantClient _client;
    private readonly IEmbeddingModel _embedder;
    private readonly string _collectionName;
    private readonly uint _vectorSize;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    /// <summary>
    /// Creates a Qdrant-backed tool memory.
    /// </summary>
    /// <param name="client">Qdrant gRPC client instance.</param>
    /// <param name="embedder">Embedding model used to vectorize tool descriptions and recall queries.</param>
    /// <param name="collectionName">Qdrant collection name. Defaults to <c>"tool_memory"</c>.</param>
    /// <param name="vectorSize">
    /// Dimensionality of the embedding vectors. Must match the embedding model output.
    /// Defaults to 16 (matches the <c>FakeEmbeddingModel</c> used in demos and tests).
    /// </param>
    public QdrantToolMemory(
        QdrantClient client,
        IEmbeddingModel embedder,
        string collectionName = "tool_memory",
        uint vectorSize = 16)
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
    public async Task UpsertAsync(ToolMemoryEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await EnsureCollectionAsync(ct).ConfigureAwait(false);

        var text = BuildEmbedText(entry);
        var vector = await _embedder.EmbedAsync(text, ct).ConfigureAwait(false);

        var point = new PointStruct
        {
            Id = StableId(entry.KitName, entry.ToolName),
            Vectors = vector.ToArray(),
            Payload =
            {
                [KitNameKey] = entry.KitName,
                [ToolNameKey] = entry.ToolName,
                [DescriptionKey] = entry.Description,
                [TagsKey] = string.Join(",", entry.Tags),
                [HealthKey] = entry.Health.ToString(),
                [HitCountKey] = entry.HitCount,
                [LastUsedKey] = entry.LastUsed.ToUnixTimeSeconds()
            }
        };

        await _client.UpsertAsync(_collectionName, [point], cancellationToken: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string kitName, string toolName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kitName);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        await EnsureCollectionAsync(ct).ConfigureAwait(false);

        await _client.DeleteAsync(
            _collectionName,
            new Filter
            {
                Must =
                {
                    Conditions.MatchKeyword(KitNameKey, kitName),
                    Conditions.MatchKeyword(ToolNameKey, toolName)
                }
            },
            cancellationToken: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ToolMemoryEntry>> RecallAsync(
        string query,
        int topK = 5,
        IReadOnlyList<string>? tagFilter = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        await EnsureCollectionAsync(ct).ConfigureAwait(false);

        var queryVector = await _embedder.EmbedAsync(query, ct).ConfigureAwait(false);

        // Build a filter that excludes Offline tools and applies optional tag narrowing
        var mustNot = new Condition[]
        {
            Conditions.MatchKeyword(HealthKey, ToolHealth.Offline.ToString())
        };

        Filter searchFilter;
        if (tagFilter is { Count: > 0 })
        {
            // At least one tag must match (OR semantics)
            var tagConditions = tagFilter
                .Select(t => Conditions.MatchText(TagsKey, t))
                .ToArray();

            searchFilter = new Filter
            {
                MustNot = { mustNot[0] },
                Should = { tagConditions }
            };
        }
        else
        {
            searchFilter = new Filter
            {
                MustNot = { mustNot[0] }
            };
        }

        var results = await _client.SearchAsync(
            collectionName: _collectionName,
            vector: queryVector,
            filter: searchFilter,
            limit: (ulong)topK,
            payloadSelector: true,
            cancellationToken: ct).ConfigureAwait(false);

        return results.Select(ToEntry).ToList();
    }

    /// <inheritdoc />
    public async Task MarkHealthAsync(
        string kitName, string toolName, ToolHealth health, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kitName);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        await EnsureCollectionAsync(ct).ConfigureAwait(false);

        await _client.SetPayloadAsync(
            _collectionName,
            new Dictionary<string, Value> { [HealthKey] = health.ToString() },
            new Filter
            {
                Must =
                {
                    Conditions.MatchKeyword(KitNameKey, kitName),
                    Conditions.MatchKeyword(ToolNameKey, toolName)
                }
            },
            cancellationToken: ct).ConfigureAwait(false);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static string BuildEmbedText(ToolMemoryEntry entry)
    {
        var tags = entry.Tags.Count > 0 ? $" Tags: {string.Join(", ", entry.Tags)}." : string.Empty;
        return $"{entry.ToolName}: {entry.Description}{tags}";
    }

    private static PointId StableId(string kitName, string toolName)
    {
        // Use the first 8 bytes of SHA-256("kitName::toolName") as a stable, collision-resistant ulong.
        // SHA-256 gives 2^64 birthday-resistance for the 64-bit slice — far beyond any realistic tool catalogue.
        var raw = $"{kitName}::{toolName}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        var id = BitConverter.ToUInt64(bytes, 0);
        // Qdrant requires id > 0; SHA-256 output being all-zero is astronomically unlikely but guard anyway.
        return new PointId { Num = id == 0 ? 1 : id };
    }

    private static ToolMemoryEntry ToEntry(ScoredPoint point)
    {
        var p = point.Payload;

        var tagsRaw = p.TryGetValue(TagsKey, out var tv) ? tv.StringValue : string.Empty;
        IReadOnlyList<string> tags = string.IsNullOrEmpty(tagsRaw)
            ? []
            : tagsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries);

        var health = p.TryGetValue(HealthKey, out var hv) &&
                     Enum.TryParse<ToolHealth>(hv.StringValue, out var parsed)
            ? parsed
            : ToolHealth.Healthy;

        var hitCount = p.TryGetValue(HitCountKey, out var hcv) ? (int)hcv.IntegerValue : 0;

        var lastUsed = p.TryGetValue(LastUsedKey, out var luv)
            ? DateTimeOffset.FromUnixTimeSeconds(luv.IntegerValue)
            : DateTimeOffset.MinValue;

        return new ToolMemoryEntry
        {
            KitName = p[KitNameKey].StringValue,
            ToolName = p[ToolNameKey].StringValue,
            Description = p[DescriptionKey].StringValue,
            Tags = tags,
            Health = health,
            HitCount = hitCount,
            LastUsed = lastUsed
        };
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
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
