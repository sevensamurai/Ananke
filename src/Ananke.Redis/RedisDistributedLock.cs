using Ananke.Abstractions.Config;
using Ananke.Abstractions.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RedLockNet;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using StackExchange.Redis;

namespace Ananke.Redis;

/// <summary>
/// Redis-backed implementation of <see cref="IDistributedLock"/> using RedLock for coordination.
/// Extends <see cref="RedisDataAdapter"/> for key-value storage, and takes its locks over the same
/// connection: the one the host passes in, or else one made from <see cref="CacheConfig"/> on first use.
/// Lock names are <c>{KeyPrefix}redlock:{resourceId}</c>, in <see cref="CacheConfig.Database"/>.
/// </summary>
public sealed class RedisDistributedLock : RedisDataAdapter, IDistributedLock
{
    private readonly ILogger<RedisDistributedLock> _logger;
    private readonly Lock _factoryGate = new();
    private RedLockFactory? _factory;
    private readonly int _lockExpirySeconds;
    private bool _disposed;

    /// <summary>
    /// RedLock's quorum retries for each acquisition; null keeps RedLock's own (3 tries, up to 400 ms apart).
    /// </summary>
    internal RedLockRetryConfiguration? RetryConfiguration { get; init; }

    /// <param name="options">Connection, database, key prefix and lock expiry settings.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="connection">
    /// An existing connection to use for values and locks instead of making one from <paramref name="options"/>.
    /// It stays the caller's: disposing the lock does not close it.
    /// </param>
    public RedisDistributedLock(
        IOptions<CacheConfig> options,
        ILogger<RedisDistributedLock>? logger = null,
        IConnectionMultiplexer? connection = null)
        : base(options.Value, connection, logger)
    {
        _logger = logger ?? NullLogger<RedisDistributedLock>.Instance;
        _lockExpirySeconds = options.Value.LockExpirySeconds ?? 5;
    }

    /// <summary>
    /// The RedLock key format for <paramref name="config"/>: <c>{KeyPrefix}redlock:{0}</c>, with braces in the
    /// prefix escaped for <see cref="string.Format(string, object?)"/>.
    /// </summary>
    internal static string LockKeyFormat(CacheConfig config) =>
        (config.KeyPrefix ?? "").Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal)
        + "redlock:{0}";

    /// <summary>
    /// Returns the RedLock factory over the shared connection, connecting on first use; null while the connection is
    /// not connected (a server it can't sign in to, or a reconnect in progress).
    /// </summary>
    private async Task<RedLockFactory?> GetFactoryAsync(CancellationToken ct)
    {
        var connection = await GetConnectionAsync(ct).ConfigureAwait(false);
        if (!connection.IsConnected) return null;
        if (_factory is not null) return _factory;

        lock (_factoryGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _factory ??= RedLockFactory.Create(
            [
                new RedLockMultiplexer(connection)
                {
                    RedisDatabase = Config.Database ?? -1,
                    RedisKeyFormat = LockKeyFormat(Config),
                },
            ], RetryConfiguration);
            return _factory;
        }
    }

    public async Task<CoordinatedActionResult<R>> RunCoordinatedActionAsync<R>(string resourceId, Func<Task<R>> action, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        IRedLock redLock;
        try
        {
            // RedLock's calls to a server that doesn't answer each wait for a client timeout, several times over.
            var factory = await GetFactoryAsync(ct).ConfigureAwait(false);
            if (factory is null)
            {
                _logger.LogDebug("Lock not acquired for resource: {ResourceId} (not connected)", resourceId);
                return CoordinatedActionResult<R>.LockFailed() with { ErrorMessage = "Not connected to Redis" };
            }

            // A zero wait skips RedLock's wait loop: it makes one acquisition, which still retries a conflicted
            // quorum (RetryConfiguration) before answering. The token ends those.
            redLock = await factory.CreateLockAsync(
                resourceId, TimeSpan.FromSeconds(_lockExpirySeconds), TimeSpan.Zero, TimeSpan.Zero, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Error acquiring lock for resource: {ResourceId}", resourceId);
            return CoordinatedActionResult<R>.LockFailed() with { ErrorMessage = ex.Message, Exception = ex };
        }

        await using (redLock.ConfigureAwait(false))
        {
            if (!redLock.IsAcquired)
            {
                _logger.LogDebug("Lock not acquired for resource: {ResourceId} ({Status})", resourceId, redLock.Status);
                return CoordinatedActionResult<R>.LockFailed();
            }

            try
            {
                var result = await action().ConfigureAwait(false);
                return CoordinatedActionResult<R>.Succeeded(result);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error during coordinated action for resource: {ResourceId}", resourceId);
                return CoordinatedActionResult<R>.Failed(ex.Message, ex);
            }
        }
    }

    public override async ValueTask DisposeAsync()
    {
        lock (_factoryGate)
        {
            if (_disposed) return;
            _disposed = true;
            _factory?.Dispose();
            _factory = null;
        }

        await base.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
