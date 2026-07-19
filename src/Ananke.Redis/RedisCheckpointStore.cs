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
    private readonly TimeProvider _clock;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = false };
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Creates a new Redis-backed checkpoint store.
    /// </summary>
    /// <param name="redis">An existing Redis connection multiplexer.</param>
    /// <param name="prefix">Key prefix for checkpoint keys. Defaults to <c>"ananke:checkpoint"</c>.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="clock">Optional time provider for TTL computation. Defaults to <see cref="TimeProvider.System"/>.</param>
    public RedisCheckpointStore(
        ConnectionMultiplexer redis,
        string prefix = "ananke:checkpoint",
        ILogger<RedisCheckpointStore>? logger = null,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        _redis = redis;
        _prefix = prefix;
        _logger = logger ?? NullLogger<RedisCheckpointStore>.Instance;
        _clock = clock ?? TimeProvider.System;
    }

    private string Key(string executionId) => $"{_prefix}:{executionId}";

    /// <summary>
    /// Pure TTL decision for <see cref="SaveAsync{TState}"/>, extracted so the expiry edge case
    /// (a checkpoint already expired by save time) is unit-testable without a Redis connection.
    /// </summary>
    internal readonly record struct TtlDecision(bool ShouldDelete, TimeSpan? Ttl);

    /// <summary>
    /// Computes whether a checkpoint due to be saved is already expired (in which case the key
    /// should be deleted rather than written with no TTL, which would make it immortal) or, if
    /// not, the TTL to apply.
    /// </summary>
    internal static TtlDecision ComputeTtlDecision(DateTimeOffset expiresAt, DateTimeOffset now)
    {
        if (expiresAt == DateTimeOffset.MaxValue)
            return new TtlDecision(ShouldDelete: false, Ttl: null);

        var ttl = expiresAt - now;
        return ttl <= TimeSpan.Zero
            ? new TtlDecision(ShouldDelete: true, Ttl: null)
            : new TtlDecision(ShouldDelete: false, Ttl: ttl);
    }

    /// <summary>
    /// Whether a loaded checkpoint's <see cref="Checkpoint{TState}.ExpiresAt"/> has passed
    /// according to <paramref name="now"/>. Checked independently of Redis's own native TTL,
    /// which may not have fired yet under clock skew or long save/GC delays.
    /// </summary>
    internal static bool IsExpired(DateTimeOffset expiresAt, DateTimeOffset now) => expiresAt <= now;

    /// <inheritdoc />
    public async Task SaveAsync<TState>(Checkpoint<TState> checkpoint, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        var db = _redis.GetDatabase();
        var key = Key(checkpoint.ExecutionId);
        var decision = ComputeTtlDecision(checkpoint.ExpiresAt, _clock.GetUtcNow());

        // Already expired by the time we'd save it — don't resurrect it as an
        // immortal key (a bare StringSetAsync with no TTL never expires).
        if (decision.ShouldDelete)
        {
            await db.KeyDeleteAsync(key);
            return;
        }

        var json = JsonSerializer.Serialize(checkpoint, WriteOptions);

        // Use atomic SET with optional expiry — avoids a TTL-less key if the process
        // crashes between a bare StringSetAsync and a subsequent KeyExpireAsync.
        if (decision.Ttl is { } t)
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

        Checkpoint<TState>? checkpoint;
        try
        {
            checkpoint = JsonSerializer.Deserialize<Checkpoint<TState>>((string)json!, ReadOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Corrupt checkpoint for execution {ExecutionId}", executionId);
            return null;
        }

        if (checkpoint is not null && IsExpired(checkpoint.ExpiresAt, _clock.GetUtcNow()))
            return null;

        return checkpoint;
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
