using Ananke.Abstractions.Channels;
using Ananke.Abstractions.Config;
using Microsoft.Extensions.DependencyInjection;

namespace Ananke.MQTT;

/// <summary>
/// DI registration extensions for Ananke.MQTT.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers MQTT-backed <see cref="IChannelReader{M, A}"/> and <see cref="IChannelWriter{A}"/>
    /// for the specified context and action types.
    /// </summary>
    /// <remarks>
    /// <paramref name="configure"/> configures the <see cref="ChannelConfig"/> options named
    /// <see cref="MqttChannelOptions.NameFor{TContext, TAction}"/>, so each registration keeps its own;
    /// read them with <c>IOptionsMonitor&lt;ChannelConfig&gt;.Get(name)</c> and pass them to
    /// <c>ConfigureAsync</c>. The unnamed <c>IOptions&lt;ChannelConfig&gt;</c> is left as it is.
    /// </remarks>
    /// <typeparam name="TContext">Message context type (any class).</typeparam>
    /// <typeparam name="TAction">Action/transition enum type used for topic routing.</typeparam>
    public static IServiceCollection AddMqtt<TContext, TAction>(
        this IServiceCollection services,
        Action<ChannelConfig> configure)
        where TContext : class
        where TAction : Enum
    {
        services.Configure(MqttChannelOptions.NameFor<TContext, TAction>(), configure);

        services.AddSingleton<IChannelReader<TContext, TAction>, MqttChannelReader<TContext, TAction>>();
        services.AddSingleton<IChannelWriter<TAction>, MqttChannelWriter<TAction>>();

        return services;
    }

    /// <summary>
    /// Registers MQTT-backed channel reader and writer with simple connection parameters.
    /// </summary>
    public static IServiceCollection AddMqtt<TContext, TAction>(
        this IServiceCollection services,
        string host,
        int port,
        string @namespace,
        string? username = null,
        string? password = null)
        where TContext : class
        where TAction : Enum
    {
        return services.AddMqtt<TContext, TAction>(c =>
        {
            c.Host = host;
            c.Port = port;
            c.Namespace = @namespace;
            c.Username = username;
            c.Password = password;
        });
    }
}
