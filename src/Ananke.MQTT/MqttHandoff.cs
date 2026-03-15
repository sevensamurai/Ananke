using Ananke.Abstractions.Channels;
using Ananke.Abstractions.Config;

namespace Ananke.MQTT;

/// <summary>
/// Registers MQTT as the <see cref="IHandoffChannel"/> transport.
/// Call <see cref="Register"/> at startup before any handoff usage.
/// </summary>
/// <example>
/// <code>
/// MqttHandoff.Register();
///
/// // Then anywhere:
/// var channel = await HandoffChannel.ConnectAsync(new ChannelConfig { Host = "localhost" });
/// </code>
/// </example>
public static class MqttHandoff
{
    /// <summary>
    /// Registers <see cref="MqttHandoffChannel"/> as the factory for
    /// <see cref="HandoffChannel.ConnectAsync"/>.
    /// </summary>
    public static void Register()
    {
        HandoffChannel.UseFactory(async (config, ct) =>
        {
            var channel = new MqttHandoffChannel();
            if (!await channel.ConfigureAsync(config, ct))
            {
                await channel.DisposeAsync();
                throw new InvalidOperationException(
                    $"Failed to connect to MQTT broker at {config.Host}:{config.Port}.");
            }
            return channel;
        });
    }
}
