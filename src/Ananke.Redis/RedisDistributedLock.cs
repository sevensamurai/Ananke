using Ananke.Abstractions.Config;
using Ananke.Abstractions.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using System.Net;

namespace Ananke.Redis;

/// <summary>
/// Redis-backed implementation of <see cref="IDistributedLock"/> using RedLock for coordination.
/// Extends <see cref="RedisDataAdapter"/> for key-value storage.
/// Connection and RedLock factory are initialized from <see cref="IOptions{CacheConfig}"/>
/// provided via DI; the Redis connection itself is deferred until first use.
/// </summary>
public sealed class RedisDistributedLock : RedisDataAdapter, IDistributedLock
{
    private readonly ILogger<RedisDistributedLock> _logger;
    private RedLockFactory? _factory;
    private int _lockExpirySeconds = 5;
    private bool _disposed;

    public RedisDistributedLock(IOptions<CacheConfig> options, ILogger<RedisDistributedLock>? logger = null)
        : base(options.Value, null)
    {
        _logger = logger ?? NullLogger<RedisDistributedLock>.Instance;
        InitializeRedLock(options.Value);
    }

    private void InitializeRedLock(CacheConfig config)
    {
        if (config.LockExpirySeconds.HasValue)
            _lockExpirySeconds = config.LockExpirySeconds.Value;

        var redlockEndPoints = new[] {
            new RedLockEndPoint(new DnsEndPoint(config.Host, config.Port))
        };
        _factory = RedLockFactory.Create(redlockEndPoints);
    }

    public async Task<CoordinatedActionResult<R>> RunCoordinatedActionAsync<R>(string resourceId, Func<Task<R>> action)
    {
        if (_factory is null)
            return CoordinatedActionResult<R>.Failed("Lock factory not initialized. Register via AddRedis() in DI.");

        try
        {
            var expiry = TimeSpan.FromSeconds(_lockExpirySeconds);
            await using var redLock = await _factory.CreateLockAsync(resourceId, expiry);

            if (!redLock.IsAcquired)
            {
                _logger.LogWarning("Failed to acquire lock for resource: {ResourceId}", resourceId);
                return CoordinatedActionResult<R>.LockFailed();
            }

            var result = await action();
            return CoordinatedActionResult<R>.Succeeded(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during coordinated action for resource: {ResourceId}", resourceId);
            return CoordinatedActionResult<R>.Failed(ex.Message, ex);
        }
    }

    public async Task<CoordinatedActionResult<R>> RunCoordinatedActionWithRetryAsync<R>(
        string resourceId,
        Func<Task<R>> action,
        int maxRetries = 3,
        int retryDelayMs = 100)
    {
        if (_factory is null)
            return CoordinatedActionResult<R>.Failed("Lock factory not initialized. Register via AddRedis() in DI.");

        var attempt = 0;
        CoordinatedActionResult<R>? lastResult = null;

        while (attempt <= maxRetries)
        {
            try
            {
                var expiry = TimeSpan.FromSeconds(_lockExpirySeconds);
                await using var redLock = await _factory.CreateLockAsync(resourceId, expiry);

                if (redLock.IsAcquired)
                {
                    var result = await action();
                    return CoordinatedActionResult<R>.Succeeded(result);
                }

                lastResult = CoordinatedActionResult<R>.LockFailed();
                _logger.LogDebug("Lock attempt {Attempt}/{MaxRetries} failed for resource: {ResourceId}",
                    attempt + 1, maxRetries, resourceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during lock attempt {Attempt}/{MaxRetries} for resource: {ResourceId}",
                    attempt + 1, maxRetries, resourceId);
                lastResult = CoordinatedActionResult<R>.Failed(ex.Message, ex);
            }

            attempt++;
            if (attempt <= maxRetries)
            {
                var delay = retryDelayMs * (int)Math.Pow(2, attempt - 1);
                await Task.Delay(delay);
            }
        }

        _logger.LogWarning("All {MaxRetries} lock attempts exhausted for resource: {ResourceId}", maxRetries, resourceId);
        return lastResult ?? CoordinatedActionResult<R>.LockFailed();
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        if (_factory is not null)
        {
            _factory.Dispose();
            _factory = null;
        }

        await base.DisposeAsync();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
