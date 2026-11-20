using Ananke.Abstractions.Config;
using Ananke.Abstractions.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;
using System.Net;
using System.Text.Json;

namespace Ananke.Redis;

/// <summary>
/// Redis-backed implementation of <see cref="IKeyValueDataAdapter"/>.
/// Uses the <see cref="IConnectionMultiplexer"/> the host passes in, or else connects on first use from the
/// <see cref="CacheConfig"/> provided via <see cref="IOptions{CacheConfig}"/>. Keys are prefixed with
/// <see cref="CacheConfig.KeyPrefix"/> and live in <see cref="CacheConfig.Database"/>.
/// </summary>
public class RedisDataAdapter : IKeyValueDataAdapter
{
    private readonly ILogger _logger;
    private readonly CacheConfig _config;
    private readonly bool _ownsConnection;
    private IConnectionMultiplexer? _redis;
    private IDatabase? _database;
    private readonly Lock _connectGate = new();
    private Task<IConnectionMultiplexer>? _connecting;
    private int _disposed;

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>
    /// DI constructor. Without <paramref name="connection"/>, the Redis connection is made from the config on the
    /// first operation, avoiding sync-over-async in the constructor, and is closed on dispose.
    /// </summary>
    /// <param name="options">Connection, database and key prefix settings.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="connection">
    /// An existing connection to use instead of making one from <paramref name="options"/>. It stays the
    /// caller's: disposing the adapter does not close it.
    /// </param>
    public RedisDataAdapter(
        IOptions<CacheConfig> options,
        ILogger<RedisDataAdapter>? logger = null,
        IConnectionMultiplexer? connection = null)
        : this(options.Value, connection, logger)
    {
    }

    /// <summary>
    /// Constructor for subclasses, which share the adapter's connection.
    /// </summary>
    private protected RedisDataAdapter(CacheConfig config, IConnectionMultiplexer? connection, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _redis = connection;
        _ownsConnection = connection is null;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>The configuration this adapter was created with.</summary>
    private protected CacheConfig Config => _config;

    /// <summary>The connection that is open or was passed in; null until the first operation otherwise.</summary>
    internal IConnectionMultiplexer? Connection => _redis;

    /// <summary>Whether a connection is open or was passed in; false until the first operation otherwise.</summary>
    internal bool HasConnection => Connection is not null;

    /// <summary>
    /// The client options for <paramref name="config"/>: its <see cref="CacheConfig.ConnectionString"/> when set,
    /// else host, port, user name, password, TLS and database; on both, the connect, sync and async timeouts that are
    /// set. The first connect makes one attempt, unless the connection string sets <c>connectRetry</c>, so it takes at
    /// most the connect timeout. A failed first connect does not abort: the client keeps trying in the background,
    /// unless the connection string sets <c>abortConnect</c>. A timeout that is zero or negative throws: the client
    /// would fail every command at once, or, at -1, wait forever.
    /// </summary>
    internal static ConfigurationOptions BuildOptions(CacheConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        ConfigurationOptions options;
        if (!string.IsNullOrWhiteSpace(config.ConnectionString))
        {
            options = ConfigurationOptions.Parse(config.ConnectionString);
            // The client trims each key, so "connectRetry = 4" sets it too.
            var keys = config.ConnectionString.Split(',')
                .Where(option => option.Contains('=', StringComparison.Ordinal))
                .Select(option => option[..option.IndexOf('=', StringComparison.Ordinal)].Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!keys.Contains("abortConnect"))
                options.AbortOnConnectFail = false;
            if (!keys.Contains("connectRetry"))
                options.ConnectRetry = 1;
        }
        else
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(config.Host);
            options = new ConfigurationOptions
            {
                EndPoints = { new DnsEndPoint(config.Host, config.Port) },
                User = config.Username,
                Password = config.Password,
                Ssl = config.Ssl,
                DefaultDatabase = config.Database,
                AbortOnConnectFail = false,
                ConnectRetry = 1,
            };
        }

        if (config.ConnectTimeoutMs is { } connectTimeout)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(connectTimeout, nameof(CacheConfig.ConnectTimeoutMs));
            options.ConnectTimeout = connectTimeout;
        }
        if (config.SyncTimeoutMs is { } syncTimeout)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(syncTimeout, nameof(CacheConfig.SyncTimeoutMs));
            options.SyncTimeout = syncTimeout;
        }
        if (config.AsyncTimeoutMs is { } asyncTimeout)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(asyncTimeout, nameof(CacheConfig.AsyncTimeoutMs));
            options.AsyncTimeout = asyncTimeout;
        }
        return options;
    }

    /// <summary>
    /// Returns the connection, connecting from the config on first use when none was passed in. Callers share one
    /// connect: <paramref name="ct"/> ends this caller's wait for it, and the connect goes on for the others.
    /// Throws when the connection cannot be set up; the next call tries again.
    /// </summary>
    private protected async Task<IConnectionMultiplexer> GetConnectionAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_redis is not null) return _redis;

        Task<IConnectionMultiplexer> connecting;
        lock (_connectGate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_connecting is null || _connecting.IsFaulted || _connecting.IsCanceled)
            {
                // Reads a failure no caller waited for, so it isn't reported as unobserved.
                _ = _connecting?.Exception;
                _connecting = ConnectFromConfigAsync();
            }
            connecting = _connecting;
        }
        return await connecting.WaitAsync(ct).ConfigureAwait(false);
    }

    private async Task<IConnectionMultiplexer> ConnectFromConfigAsync()
    {
        _redis = await ConnectionMultiplexer.ConnectAsync(BuildOptions(_config)).ConfigureAwait(false);
        return _redis;
    }

    private async Task<IDatabase> GetDatabaseAsync(CancellationToken ct)
    {
        if (_database is not null)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return _database;
        }

        var connection = await GetConnectionAsync(ct).ConfigureAwait(false);
        var database = connection.GetDatabase(_config.Database ?? -1);
        _database = string.IsNullOrEmpty(_config.KeyPrefix) ? database : database.WithKeyPrefix(_config.KeyPrefix);
        return _database;
    }

    /// <summary>
    /// Runs one Redis command until <paramref name="ct"/> is cancelled; a command no longer waited on keeps running.
    /// A failure other than that cancellation is logged at Debug with the operation and key, then rethrown; the caller
    /// decides how loud it is.
    /// </summary>
    private async Task<TResult> RunAsync<TResult>(
        string operation, string key, Func<IDatabase, Task<TResult>> command, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var db = await GetDatabaseAsync(ct).ConfigureAwait(false);
            return await command(db).WaitForAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "Redis {Operation} failed for key '{Key}'", operation, key);
            throw;
        }
    }

    public async Task<string?> GetValueAsync(string key, CancellationToken ct = default) =>
        await RunAsync("GET", key, async db => (string?)await db.StringGetAsync(key).ConfigureAwait(false), ct)
            .ConfigureAwait(false);

    public async Task<T?> GetValueAsync<T>(string key, CancellationToken ct = default)
    {
        var value = await GetValueAsync(key, ct).ConfigureAwait(false);
        if (value is null) return default;

        try
        {
            return JsonSerializer.Deserialize<T>(value);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            _logger.LogDebug(ex, "Redis DESERIALIZE failed for key '{Key}'", key);
            throw;
        }
    }

    public Task SetValueAsync(string key, string value, CancellationToken ct = default) =>
        SetValueAsync(key, value, null, ct);

    public Task SetValueAsync<T>(string key, T value, CancellationToken ct = default) =>
        SetValueAsync(key, JsonSerializer.Serialize(value), null, ct);

    public Task SetValueAsync(string key, string value, TimeSpan? expiry, CancellationToken ct = default)
    {
        if (expiry is not { } ttl)
            return RunAsync("SET", key, db => db.StringSetAsync(key, value), ct);

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero, nameof(expiry));
        // Redis takes whole milliseconds: a shorter positive expiry would be sent as 0, which the server rejects.
        if (ttl < TimeSpan.FromMilliseconds(1))
            ttl = TimeSpan.FromMilliseconds(1);
        return RunAsync("SET", key, db => db.StringSetAsync(key, value, ttl), ct);
    }

    public Task SetValueAsync<T>(string key, T value, TimeSpan? expiry, CancellationToken ct = default) =>
        SetValueAsync(key, JsonSerializer.Serialize(value), expiry, ct);

    public Task<bool> RemoveAsync(string key, CancellationToken ct = default) =>
        RunAsync("REMOVE", key, db => db.KeyDeleteAsync(key), ct);

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
        RunAsync("EXISTS", key, db => db.KeyExistsAsync(key), ct);

    public virtual async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        // Waits for a connect in progress, so the connection it makes is closed here rather than left open.
        Task<IConnectionMultiplexer>? connecting;
        lock (_connectGate)
            connecting = _connecting;
        if (connecting is not null)
            await ((Task)connecting).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        if (_ownsConnection && _redis is not null)
        {
            await _redis.CloseAsync().ConfigureAwait(false);
            await _redis.DisposeAsync().ConfigureAwait(false);
        }
        _redis = null;
        _database = null;

        GC.SuppressFinalize(this);
    }
}
