using Ananke.Abstractions.Config;
using Ananke.Abstractions.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ananke.Redis;

/// <summary>
/// DI registration extensions for Ananke.Redis.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Redis-backed <see cref="IDistributedLock"/> and <see cref="IKeyValueDataAdapter"/>
    /// using the provided <see cref="CacheConfig"/>.
    /// Replaces any previously registered <see cref="IDistributedLock"/> (e.g. the in-memory
    /// fallback from <c>AddStateMachine</c>), so call order does not matter.
    /// </summary>
    public static IServiceCollection AddRedis(
        this IServiceCollection services,
        Action<CacheConfig> configure)
    {
        services.Configure(configure);

        services.RemoveAll<IDistributedLock>();
        services.AddSingleton<RedisDistributedLock>();
        services.AddSingleton<IDistributedLock>(sp => sp.GetRequiredService<RedisDistributedLock>());
        services.AddSingleton<IKeyValueDataAdapter>(sp => sp.GetRequiredService<RedisDistributedLock>());

        return services;
    }

    /// <summary>
    /// Registers Redis-backed <see cref="IDistributedLock"/> and <see cref="IKeyValueDataAdapter"/>
    /// with simple connection parameters.
    /// </summary>
    public static IServiceCollection AddRedis(
        this IServiceCollection services,
        string host,
        int port = 6379,
        string? password = null,
        int? lockExpirySeconds = null)
    {
        return services.AddRedis(c =>
        {
            c.Host = host;
            c.Port = port;
            c.Password = password;
            c.LockExpirySeconds = lockExpirySeconds;
        });
    }
}
