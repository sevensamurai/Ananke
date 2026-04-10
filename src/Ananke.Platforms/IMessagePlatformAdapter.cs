namespace Ananke.Platforms;

/// <summary>
/// Adapter that bridges a messaging platform to Ananke's orchestration engine.
/// Implementations handle platform connection lifecycle (WebSocket, HTTP webhooks, etc.)
/// and dispatch incoming events to the registered <see cref="IPlatformMessageHandler"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each messaging platform (Slack, Discord, Teams) provides a concrete implementation
/// in its own package (e.g. <c>Ananke.Platforms.Slack</c>). The adapter normalizes
/// platform-specific events into <see cref="PlatformMessage"/> instances and forwards
/// them to the handler for workflow routing.
/// </para>
/// <para>
/// Adapters are typically registered as hosted services via DI extensions
/// (e.g. <c>services.AddAnankeSlack(...)</c>) and started/stopped with the application host.
/// </para>
/// </remarks>
public interface IMessagePlatformAdapter : IAsyncDisposable
{
    /// <summary>
    /// Starts the adapter — connects to the platform (WebSocket, registers webhooks, etc.)
    /// and begins dispatching incoming messages to the handler.
    /// </summary>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops the adapter — disconnects from the platform and ceases message dispatch.
    /// </summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>Whether the adapter is currently connected and receiving events.</summary>
    bool IsConnected { get; }

    /// <summary>The response sink for sending messages back to this platform.</summary>
    IPlatformResponseSink ResponseSink { get; }
}
