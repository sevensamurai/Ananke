using Ananke.Abstractions.Config;

namespace Ananke.Abstractions.Channels;

/// <summary>
/// Factory for creating configured <see cref="IHandoffChannel"/> instances.
/// Register a channel provider at startup (e.g. <c>MqttHandoff.Register()</c>),
/// then call <see cref="ConnectAsync"/> to obtain a connected channel.
/// </summary>
/// <example>
/// <code>
/// // At startup — register the transport once
/// MqttHandoff.Register();
///
/// // Anywhere — create a connected channel
/// var channel = await HandoffChannel.ConnectAsync(new ChannelConfig { Host = "localhost" });
/// </code>
/// </example>
public static class HandoffChannel
{
    private static Func<ChannelConfig, CancellationToken, Task<IHandoffChannel>>? _factory;

    /// <summary>
    /// Registers the factory function used by <see cref="ConnectAsync"/> to create channels.
    /// Called by transport packages (e.g. <c>MqttHandoff.Register()</c>).
    /// </summary>
    public static void UseFactory(
        Func<ChannelConfig, CancellationToken, Task<IHandoffChannel>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <summary>
    /// Creates and connects a new <see cref="IHandoffChannel"/> using the registered factory.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no factory has been registered or the connection fails.
    /// </exception>
    public static async Task<IHandoffChannel> ConnectAsync(
        ChannelConfig config,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (_factory is null)
            throw new InvalidOperationException(
                "No handoff channel provider registered. " +
                "Call a provider registration method (e.g. MqttHandoff.Register()) at startup.");

        return await _factory(config, ct);
    }
}
