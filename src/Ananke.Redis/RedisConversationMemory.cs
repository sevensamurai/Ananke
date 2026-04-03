using System.Text.Json;
using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace Ananke.Redis;

/// <summary>
/// Redis-backed <see cref="IConversationMemory"/> for distributed multi-turn agent conversations.
/// Each session is stored as a Redis List keyed by <c>{prefix}:{sessionId}</c> with optional TTL.
/// </summary>
/// <remarks>
/// Messages are serialized to JSON. TTL is applied per-session and refreshed on every write,
/// keeping active conversations alive while expired ones are cleaned up by Redis automatically.
/// </remarks>
public sealed class RedisConversationMemory : IConversationMemory
{
    private readonly ConnectionMultiplexer _redis;
    private readonly ILogger<RedisConversationMemory> _logger;
    private readonly string _prefix;
    private readonly TimeSpan? _ttl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Creates a new Redis-backed conversation memory.
    /// </summary>
    /// <param name="redis">An existing Redis connection multiplexer.</param>
    /// <param name="prefix">Key prefix for session keys. Defaults to <c>"ananke:memory"</c>.</param>
    /// <param name="ttl">
    /// Optional time-to-live per session. When set, Redis automatically expires keys
    /// that have not been written to within this duration. When <see langword="null"/>,
    /// sessions persist until explicitly cleared.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    public RedisConversationMemory(
        ConnectionMultiplexer redis,
        string prefix = "ananke:memory",
        TimeSpan? ttl = null,
        ILogger<RedisConversationMemory>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        _redis = redis;
        _prefix = prefix;
        _ttl = ttl;
        _logger = logger ?? NullLogger<RedisConversationMemory>.Instance;
    }

    /// <inheritdoc />
    public async Task AddAsync(string sessionId, IEnumerable<AgentMessage> messages, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(messages);

        var key = Key(sessionId);
        var values = messages.Select(m => (RedisValue)JsonSerializer.Serialize(m, JsonOptions)).ToArray();

        if (values.Length == 0) return;

        try
        {
            var db = _redis.GetDatabase();
            await db.ListRightPushAsync(key, values);
            await RefreshTtlAsync(db, key);
            _logger.LogDebug("Added {Count} message(s) to session {SessionId}", values.Length, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add {Count} message(s) to session {SessionId}", values.Length, sessionId);
        }
    }

    /// <inheritdoc />
    public async Task AddAsync(string sessionId, AgentMessage message, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(message);

        var key = Key(sessionId);

        try
        {
            var db = _redis.GetDatabase();
            await db.ListRightPushAsync(key, JsonSerializer.Serialize(message, JsonOptions));
            await RefreshTtlAsync(db, key);
            _logger.LogDebug("Added message to session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add message to session {SessionId}", sessionId);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentMessage>> GetHistoryAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var key = Key(sessionId);
        RedisValue[] values;

        try
        {
            var db = _redis.GetDatabase();
            values = await db.ListRangeAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve history for session {SessionId}", sessionId);
            return [];
        }

        var messages = new List<AgentMessage>(values.Length);
        foreach (var value in values)
        {
            var msg = JsonSerializer.Deserialize<AgentMessage>(value.ToString(), JsonOptions);
            if (msg is not null)
                messages.Add(msg);
            else
                _logger.LogWarning("Failed to deserialize message in session {SessionId}, entry skipped", sessionId);
        }

        return messages;
    }

    /// <inheritdoc />
    public async Task ClearAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var key = Key(sessionId);

        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(key);
            _logger.LogDebug("Cleared session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear session {SessionId}", sessionId);
        }
    }

    /// <inheritdoc />
    public Task CleanupExpiredAsync(CancellationToken ct = default)
    {
        // Redis handles TTL expiry natively — no manual cleanup needed.
        return Task.CompletedTask;
    }

    private string Key(string sessionId) => $"{_prefix}:{sessionId}";

    private async Task RefreshTtlAsync(IDatabase db, string key)
    {
        if (_ttl.HasValue)
            await db.KeyExpireAsync(key, _ttl.Value);
    }
}
