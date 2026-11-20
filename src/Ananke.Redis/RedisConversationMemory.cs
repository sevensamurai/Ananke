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
/// Messages are serialized to JSON. TTL is applied per-session and refreshed on every write, in the
/// same transaction as the append, keeping active conversations alive while expired ones are
/// cleaned up by Redis automatically. Cancelling an operation stops the wait for the server; a
/// write already sent may still be applied.
/// </remarks>
public sealed class RedisConversationMemory : IConversationMemory
{
    private readonly IConnectionMultiplexer _redis;
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
    /// <param name="redis">An existing Redis connection, which the caller keeps owning.</param>
    /// <param name="prefix">Key prefix for session keys. Defaults to <c>"ananke:memory"</c>.</param>
    /// <param name="ttl">
    /// Optional time-to-live per session. When set, Redis automatically expires keys
    /// that have not been written to within this duration. When <see langword="null"/>,
    /// sessions persist until explicitly cleared.
    /// </param>
    /// <param name="logger">Optional logger.</param>
    public RedisConversationMemory(
        IConnectionMultiplexer redis,
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

        ct.ThrowIfCancellationRequested();

        var key = Key(sessionId);
        var values = messages.Select(m => (RedisValue)JsonSerializer.Serialize(m, JsonOptions)).ToArray();

        if (values.Length == 0) return;

        try
        {
            await AppendAsync(key, values, ct).ConfigureAwait(false);
            _logger.LogDebug("Added {Count} message(s) to session {SessionId}", values.Length, sessionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to add {Count} message(s) to session {SessionId}", values.Length, sessionId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task AddAsync(string sessionId, AgentMessage message, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(message);

        ct.ThrowIfCancellationRequested();

        var key = Key(sessionId);

        try
        {
            await AppendAsync(key, [JsonSerializer.Serialize(message, JsonOptions)], ct).ConfigureAwait(false);
            _logger.LogDebug("Added message to session {SessionId}", sessionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to add message to session {SessionId}", sessionId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentMessage>> GetHistoryAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        ct.ThrowIfCancellationRequested();

        var key = Key(sessionId);
        RedisValue[] values;

        try
        {
            var db = _redis.GetDatabase();
            values = await db.ListRangeAsync(key).WaitForAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Rethrown: an empty history reads as a new conversation, and the caller would carry on
            // without anything it had been told.
            _logger.LogError(ex, "Failed to retrieve history for session {SessionId}", sessionId);
            throw;
        }

        var messages = new List<AgentMessage>(values.Length);
        foreach (var value in values)
        {
            AgentMessage? msg;

            try
            {
                msg = JsonSerializer.Deserialize<AgentMessage>(value.ToString(), JsonOptions);
            }
            catch (JsonException)
            {
                msg = null;
            }

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

        ct.ThrowIfCancellationRequested();

        var key = Key(sessionId);

        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(key).WaitForAsync(ct).ConfigureAwait(false);
            _logger.LogDebug("Cleared session {SessionId}", sessionId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to clear session {SessionId}", sessionId);
            throw;
        }
    }

    /// <inheritdoc />
    public Task CleanupExpiredAsync(CancellationToken ct = default)
    {
        // Redis handles TTL expiry natively — no manual cleanup needed.
        return ct.IsCancellationRequested ? Task.FromCanceled(ct) : Task.CompletedTask;
    }

    private string Key(string sessionId) => $"{_prefix}:{sessionId}";

    /// <summary>
    /// Appends <paramref name="values"/> to the session list. With a TTL, the append and the expiry go in one
    /// transaction (<c>MULTI</c>/<c>EXEC</c>), so a session is never written without its expiry.
    /// </summary>
    private async Task AppendAsync(string key, RedisValue[] values, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        if (_ttl is not { } ttl)
        {
            await db.ListRightPushAsync(key, values).WaitForAsync(ct).ConfigureAwait(false);
            return;
        }

        var transaction = db.CreateTransaction();
        var push = transaction.ListRightPushAsync(key, values);
        var expire = transaction.KeyExpireAsync(key, ttl);
        // Awaited below only once the transaction commits.
        RedisCommand.Observe(push);
        RedisCommand.Observe(expire);
        var committed = await transaction.ExecuteAsync().WaitForAsync(ct).ConfigureAwait(false);
        if (!committed)
            throw new RedisException($"The append to '{key}' was not committed");
        await Task.WhenAll(push, expire).ConfigureAwait(false);
    }
}
