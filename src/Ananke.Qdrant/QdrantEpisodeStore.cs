using System.Text.Json;
using Ananke.Learning.Episodes;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Ananke.Qdrant;

/// <summary>
/// Qdrant-backed <see cref="IEpisodeStore"/> for persistent episode storage.
/// Automatically creates the collection on first use if it does not exist.
/// </summary>
/// <remarks>
/// <para>
/// Each episode is stored as a Qdrant point. Point IDs are deterministic
/// UUIDs derived from the episode ID string via UUID v5 hashing, ensuring
/// stable upsert/dedup behavior.
/// </para>
/// <para>
/// Episode steps and metadata are serialized as JSON strings in the payload.
/// Scalar fields (<c>terminal_reward</c>, <c>completed_at</c>, <c>started_at</c>)
/// are stored as native Qdrant types for indexed filtering and ordering.
/// </para>
/// <para>
/// A zero-dimension vector is used since episodes do not require semantic search.
/// The collection uses a dummy single-dimension vector to satisfy the Qdrant schema.
/// Browse operations use <see cref="QdrantClient.ScrollAsync"/> with payload filters.
/// </para>
/// </remarks>
public sealed class QdrantEpisodeStore : IEpisodeStore
{
    private const string TerminalRewardKey = "terminal_reward";
    private const string StartedAtKey = "started_at";
    private const string CompletedAtKey = "completed_at";
    private const string StepsKey = "steps";
    private const string MetadataKey = "metadata";
    private const string EntityIdKey = "entity_id";

    // RFC 4122 §4.3 — predefined DNS namespace UUID used for deterministic v5 UUID generation
    private static readonly Guid UuidNamespaceDns = new("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly QdrantClient _client;
    private readonly string _collectionName;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    /// <summary>
    /// Creates a Qdrant-backed episode store.
    /// </summary>
    /// <param name="client">Qdrant gRPC client instance.</param>
    /// <param name="collectionName">
    /// Qdrant collection name. Default is <c>"episodes"</c>.
    /// </param>
    public QdrantEpisodeStore(
        QdrantClient client,
        string collectionName = "episodes")
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);

        _client = client;
        _collectionName = collectionName;
    }

    /// <inheritdoc />
    public async Task<Episode> CommitAsync(Episode episode, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(episode);
        await EnsureCollectionAsync(ct);

        var point = BuildPoint(episode);
        await _client.UpsertAsync(_collectionName, [point], cancellationToken: ct);
        return episode;
    }

    /// <inheritdoc />
    public async Task<Episode?> GetAsync(string episodeId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(episodeId);
        await EnsureCollectionAsync(ct);

        var points = await _client.RetrieveAsync(
            _collectionName,
            [ToPointId(episodeId)],
            withPayload: true,
            cancellationToken: ct);

        var point = points.FirstOrDefault();
        return point is null ? null : MapPayloadToEpisode(episodeId, point.Payload);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Episode>> BrowseAsync(
        int offset, int limit, string? entityId = null,
        CancellationToken ct = default)
    {
        await EnsureCollectionAsync(ct);

        Filter? filter = entityId is not null
            ? new Filter { Must = { Conditions.MatchKeyword(EntityIdKey, entityId) } }
            : null;

        // Scroll through episodes; client-side sort by CompletedAt desc
        var result = await _client.ScrollAsync(
            _collectionName,
            filter: filter,
            limit: (uint)(offset + limit),
            payloadSelector: true,
            cancellationToken: ct);

        return result.Result
            .Select(p => MapPayloadToEpisode(p.Id.Uuid, p.Payload))
            .OrderByDescending(e => e.CompletedAt)
            .Skip(offset)
            .Take(limit)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Episode>> BrowseByOutcomeAsync(
        float minReward, float maxReward, int offset, int limit,
        string? entityId = null, CancellationToken ct = default)
    {
        await EnsureCollectionAsync(ct);

        var filter = new Filter
        {
            Must =
            {
                Conditions.Range(TerminalRewardKey,
                    new global::Qdrant.Client.Grpc.Range
                    {
                        Gte = minReward,
                        Lte = maxReward
                    })
            }
        };

        if (entityId is not null)
            filter.Must.Add(Conditions.MatchKeyword(EntityIdKey, entityId));

        var result = await _client.ScrollAsync(
            _collectionName,
            filter: filter,
            limit: (uint)(offset + limit),
            payloadSelector: true,
            cancellationToken: ct);

        return result.Result
            .Select(p => MapPayloadToEpisode(p.Id.Uuid, p.Payload))
            .OrderByDescending(e => e.CompletedAt)
            .Skip(offset)
            .Take(limit)
            .ToList();
    }

    // ── Collection initialization ────────────────────────────────

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
                // Episodes don't need semantic search — use a minimal
                // single-dimension vector to satisfy the Qdrant schema.
                await _client.CreateCollectionAsync(
                    _collectionName,
                    new VectorParams { Size = 1, Distance = Distance.Cosine },
                    cancellationToken: ct);

                await _client.CreatePayloadIndexAsync(
                    _collectionName, TerminalRewardKey,
                    PayloadSchemaType.Float, cancellationToken: ct);

                await _client.CreatePayloadIndexAsync(
                    _collectionName, CompletedAtKey,
                    PayloadSchemaType.Integer, cancellationToken: ct);

                await _client.CreatePayloadIndexAsync(
                    _collectionName, StartedAtKey,
                    PayloadSchemaType.Integer, cancellationToken: ct);

                await _client.CreatePayloadIndexAsync(
                    _collectionName, EntityIdKey,
                    PayloadSchemaType.Keyword, cancellationToken: ct);
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    // ── Point building ───────────────────────────────────────────

    private static PointStruct BuildPoint(Episode episode)
    {
        var payload = new Dictionary<string, Value>
        {
            [TerminalRewardKey] = (double)episode.TerminalReward,
            [StartedAtKey] = episode.StartedAt.ToUnixTimeSeconds(),
            [CompletedAtKey] = episode.CompletedAt.ToUnixTimeSeconds(),
            [StepsKey] = JsonSerializer.Serialize(episode.Steps, JsonOptions),
            [MetadataKey] = JsonSerializer.Serialize(episode.Metadata, JsonOptions)
        };

        if (episode.EntityId is not null)
            payload[EntityIdKey] = episode.EntityId;

        return new PointStruct
        {
            Id = ToPointId(episode.Id),
            // Dummy vector — episodes use payload filtering, not vector search.
            Vectors = new float[] { 0f },
            Payload = { payload }
        };
    }

    // ── Payload mapping ──────────────────────────────────────────

    private static Episode MapPayloadToEpisode(
        string id, IReadOnlyDictionary<string, Value> payload)
    {
        var stepsJson = GetString(payload, StepsKey);
        var steps = string.IsNullOrEmpty(stepsJson)
            ? []
            : JsonSerializer.Deserialize<List<EpisodeStep>>(stepsJson, JsonOptions) ?? [];

        var metadataJson = GetString(payload, MetadataKey);
        var metadata = string.IsNullOrEmpty(metadataJson)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson, JsonOptions)
              ?? new Dictionary<string, string>();

        return new Episode
        {
            Id = id,
            Steps = steps,
            TerminalReward = (float)GetDouble(payload, TerminalRewardKey),
            StartedAt = DateTimeOffset.FromUnixTimeSeconds(GetLong(payload, StartedAtKey)),
            CompletedAt = DateTimeOffset.FromUnixTimeSeconds(GetLong(payload, CompletedAtKey)),
            Metadata = metadata,
            EntityId = payload.TryGetValue(EntityIdKey, out var eid)
                && eid.KindCase == Value.KindOneofCase.StringValue
                && eid.StringValue.Length > 0
                ? eid.StringValue : null
        };
    }

    // ── Payload helpers ──────────────────────────────────────────

    private static string GetString(
        IReadOnlyDictionary<string, Value> payload, string key, string fallback = "")
    {
        if (payload.TryGetValue(key, out var v) && v.KindCase == Value.KindOneofCase.StringValue)
            return v.StringValue;
        return fallback;
    }

    private static double GetDouble(
        IReadOnlyDictionary<string, Value> payload, string key, double fallback = 0)
    {
        if (!payload.TryGetValue(key, out var v)) return fallback;
        return v.KindCase switch
        {
            Value.KindOneofCase.DoubleValue => v.DoubleValue,
            Value.KindOneofCase.IntegerValue => v.IntegerValue,
            _ => fallback
        };
    }

    private static long GetLong(
        IReadOnlyDictionary<string, Value> payload, string key, long fallback = 0)
    {
        if (!payload.TryGetValue(key, out var v)) return fallback;
        return v.KindCase switch
        {
            Value.KindOneofCase.IntegerValue => v.IntegerValue,
            Value.KindOneofCase.DoubleValue => (long)v.DoubleValue,
            _ => fallback
        };
    }

    // ── Deterministic point IDs ──────────────────────────────────

    private static PointId ToPointId(string episodeId) =>
        new() { Uuid = ToUuidV5(episodeId).ToString("D") };

    /// <summary>
    /// RFC 4122 §4.3 — generates a version 5 UUID using SHA-1 hashing of
    /// the namespace UUID and name.
    /// </summary>
    private static Guid ToUuidV5(string name)
    {
        var namespaceBytes = UuidNamespaceDns.ToByteArray();
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
