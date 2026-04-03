using Ananke.Abstractions.Config;
using Ananke.Abstractions.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Net;
using System.Text.Json;

namespace Ananke.Redis;

/// <summary>
/// Redis-backed implementation of <see cref="IKeyValueDataAdapter"/>.
/// Connection is established lazily on first use from the <see cref="CacheConfig"/>
/// provided via <see cref="IOptions{CacheConfig}"/>.
/// </summary>
public class RedisDataAdapter : IKeyValueDataAdapter
{
    private readonly ILogger<RedisDataAdapter> _logger;
    private ConnectionMultiplexer? _redis;
    private CacheConfig? _deferredConfig;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// DI constructor. Stores the config for deferred connection — the Redis connection
    /// is established lazily on the first operation, avoiding sync-over-async in the constructor.
    /// </summary>
    public RedisDataAdapter(IOptions<CacheConfig> options, ILogger<RedisDataAdapter>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options.Value);
        _deferredConfig = options.Value;
        _logger = logger ?? NullLogger<RedisDataAdapter>.Instance;
    }

    /// <summary>
    /// Internal constructor for subclasses that manage their own config lifecycle.
    /// </summary>
    private protected RedisDataAdapter(CacheConfig? config, ILogger<RedisDataAdapter>? logger)
    {
        _deferredConfig = config;
        _logger = logger ?? NullLogger<RedisDataAdapter>.Instance;
    }

    private async Task ConnectAsync(CacheConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Host);

        var configOptions = new ConfigurationOptions
        {
            EndPoints = { new DnsEndPoint(config.Host, config.Port) },
            Password = config.Password,
            AbortOnConnectFail = false,
            Ssl = false,
        };
        _redis = await ConnectionMultiplexer.ConnectAsync(configOptions);
        _deferredConfig = null;
    }

    /// <summary>
    /// Establishes the Redis connection from deferred config on first use.
    /// No-op if already connected.
    /// </summary>
    private async Task EnsureConnectedAsync()
    {
        if (_redis is not null || _deferredConfig is null) return;

        await _connectLock.WaitAsync();
        try
        {
            if (_redis is not null || _deferredConfig is null) return;
            await ConnectAsync(_deferredConfig);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis deferred connection failed");
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async Task<string?> GetValueAsync(string key)
    {
        await EnsureConnectedAsync();
        if (_redis is null)
        {
            _logger.LogWarning("Redis GET skipped for key '{Key}': connection not established", key);
            return null;
        }

        try
        {
            var db = _redis.GetDatabase();
            return await db.StringGetAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis GET error for key '{Key}'", key);
        }
        return default;
    }

    public async Task<T?> GetValueAsync<T>(string key)
    {
        var value = await GetValueAsync(key);
        if (string.IsNullOrWhiteSpace(value)) return default;

        try
        {
            return JsonSerializer.Deserialize<T>(value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis DESERIALIZE error for key '{Key}'", key);
        }
        return default;
    }

    public async Task SetValueAsync(string key, string value)
    {
        await EnsureConnectedAsync();
        if (_redis is null)
        {
            _logger.LogWarning("Redis SET skipped for key '{Key}': connection not established", key);
            return;
        }

        try
        {
            var db = _redis.GetDatabase();
            await db.StringSetAsync(key, value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis SET error for key '{Key}'", key);
        }
    }

    public async Task SetValueAsync<T>(string key, T value)
    {
        await EnsureConnectedAsync();
        if (_redis is null)
        {
            _logger.LogWarning("Redis SET<T> skipped for key '{Key}': connection not established", key);
            return;
        }

        try
        {
            var db = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(value);
            await db.StringSetAsync(key, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis SET<T> error for key '{Key}'", key);
        }
    }

    public async Task<bool> RemoveAsync(string key)
    {
        await EnsureConnectedAsync();
        if (_redis is null)
        {
            _logger.LogWarning("Redis REMOVE skipped for key '{Key}': connection not established", key);
            return false;
        }

        try
        {
            var db = _redis.GetDatabase();
            return await db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis REMOVE error for key '{Key}'", key);
        }
        return false;
    }

    public async Task<bool> ExistsAsync(string key)
    {
        await EnsureConnectedAsync();
        if (_redis is null)
        {
            _logger.LogWarning("Redis EXISTS skipped for key '{Key}': connection not established", key);
            return false;
        }

        try
        {
            var db = _redis.GetDatabase();
            return await db.KeyExistsAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis EXISTS error for key '{Key}'", key);
        }
        return false;
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        if (_redis is not null)
        {
            await _redis.CloseAsync();
            await _redis.DisposeAsync();
            _redis = null;
        }

        _connectLock.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
