using System.Text.Json;
using Ananke.Orchestration.Checkpointing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace Ananke.Redis;

/// <summary>
/// Redis-backed <see cref="ICheckpointStore"/> for distributed workflow checkpointing.
/// Each checkpoint is stored as a JSON string keyed by <c>{prefix}:{executionId}</c>.
/// Expiry is handled natively via Redis <c>EXPIREAT</c> when
/// <see cref="Checkpoint{TState}.ExpiresAt"/> is set.
/// </summary>
/// <remarks>
/// Serialization uses <see cref="JsonSerializer"/> with case-insensitive deserialization.
/// </remarks>
public sealed class RedisCheckpointStore : ICheckpointStore
{
    private readonly ConnectionMultiplexer _redis;
    private readonly ILogger<RedisCheckpointStore> _logger;
    private readonly string _prefix;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = false };
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Creates a new Redis-backed checkpoint store.
    /// </summary>
    /// <param name="redis">An existing Redis connection multiplexer.</param>
    /// <param name="prefix">Key prefix for checkpoint keys. Defaults to <c>"ananke:checkpoint"</c>.</param>
    /// <param name="logger">Optional logger.</param>
    public RedisCheckpointStore(
        ConnectionMultiplexer redis,
        string prefix = "ananke:checkpoint",
        ILogger<RedisCheckpointStore>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        _redis = redis;
        _prefix = prefix;
        _logger = logger ?? NullLogger<RedisCheckpointStore>.Instance;
    }

    private string Key(string executionId) => $"{_prefix}:{executionId}";

    /// <inheritdoc />
    public async Task SaveAsync<TState>(Checkpoint<TState> checkpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        var db = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(checkpoint, WriteOptions);
        var key = Key(checkpoint.ExecutionId);

        TimeSpan? ttl = checkpoint.ExpiresAt != DateTimeOffset.MaxValue
            ? checkpoint.ExpiresAt.UtcDateTime - DateTime.UtcNow
            : null;

        // Use atomic SET with optional expiry — avoids a TTL-less key if the process
        // crashes between a bare StringSetAsync and a subsequent KeyExpireAsync.
        if (ttl is { } t && t > TimeSpan.Zero)
            await db.StringSetAsync(key, json, t);
        else
            await db.StringSetAsync(key, json);
    }

    /// <inheritdoc />
    public async Task<Checkpoint<TState>?> LoadAsync<TState>(string executionId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var json = await db.StringGetAsync(Key(executionId));

        if (json.IsNullOrEmpty)
            return null;

        try
        {
            return JsonSerializer.Deserialize<Checkpoint<TState>>((string)json!, ReadOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Corrupt checkpoint for execution {ExecutionId}", executionId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string executionId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(Key(executionId));
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string executionId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        return await db.KeyExistsAsync(Key(executionId));
    }

    /// <inheritdoc />
    /// <remarks>
    /// No-op for the Redis implementation — expiry is handled natively by Redis TTL
    /// set during <see cref="SaveAsync{TState}"/>. Keys are automatically removed by Redis
    /// when they expire.
    /// </remarks>
    public Task CleanupExpiredAsync(CancellationToken ct = default) => Task.CompletedTask;
}
