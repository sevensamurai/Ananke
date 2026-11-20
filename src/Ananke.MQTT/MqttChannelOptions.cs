using Ananke.Abstractions.Config;

namespace Ananke.MQTT;

/// <summary>
/// The names of the <see cref="ChannelConfig"/> options that
/// <see cref="ServiceCollectionExtensions.AddMqtt{TContext, TAction}(Microsoft.Extensions.DependencyInjection.IServiceCollection, Action{ChannelConfig})"/>
/// configures, one per message and action type.
/// </summary>
/// <example>
/// <code>
/// var config = provider.GetRequiredService&lt;IOptionsMonitor&lt;ChannelConfig&gt;&gt;()
///     .Get(MqttChannelOptions.NameFor&lt;MyContext, MyAction&gt;());
/// await reader.ConfigureAsync(config, worker);
/// </code>
/// </example>
public static class MqttChannelOptions
{
    /// <summary>The options name for <typeparamref name="TContext"/> and <typeparamref name="TAction"/>.</summary>
    public static string NameFor<TContext, TAction>()
        where TContext : class
        where TAction : Enum
        => $"Ananke.MQTT:{typeof(TContext).FullName}:{typeof(TAction).FullName}";
}
