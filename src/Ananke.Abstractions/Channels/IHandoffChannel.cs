namespace Ananke.Abstractions.Channels;

/// <summary>
/// Request-response messaging abstraction for agent-to-agent handoff.
/// The sender publishes a message and awaits a correlated response; the responder
/// subscribes via <see cref="SubscribeAsync{TMessage, TResponse}"/> or completes
/// individual requests via <see cref="CompleteAsync{TResponse}"/>.
/// </summary>
/// <remarks>
/// Implementations: <c>InMemoryHandoffChannel</c> (testing, in Ananke.Orchestration) and
/// <c>MqttHandoffChannel</c> (production, in Ananke.MQTT).
/// Use <see cref="HandoffChannel.ConnectAsync"/> to create instances via the registered factory.
/// </remarks>
public interface IHandoffChannel : IAsyncDisposable
{
    /// <summary>
    /// Sends a message to the specified topic and waits for a correlated response.
    /// </summary>
    /// <typeparam name="TMessage">The outgoing message type.</typeparam>
    /// <typeparam name="TResponse">The expected response type.</typeparam>
    /// <param name="topic">The destination topic (e.g. a queue name).</param>
    /// <param name="correlationId">
    /// Unique correlation ID for this request, typically derived from the workflow
    /// execution ID and job name.
    /// </param>
    /// <param name="message">The message payload to send.</param>
    /// <param name="timeout">Maximum time to wait for a response.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The deserialized response.</returns>
    Task<TResponse> SendAsync<TMessage, TResponse>(
        string topic,
        string correlationId,
        TMessage message,
        TimeSpan timeout,
        CancellationToken ct = default)
        where TMessage : class
        where TResponse : class;

    /// <summary>
    /// Completes a pending handoff by providing the response for the given correlation ID.
    /// Called by the responding agent or test code to unblock the waiting
    /// <see cref="SendAsync{TMessage, TResponse}"/> call.
    /// </summary>
    Task CompleteAsync<TResponse>(
        string topic,
        string correlationId,
        TResponse response,
        CancellationToken ct = default)
        where TResponse : class;

    /// <summary>
    /// Subscribes to incoming handoff requests on the specified topic.
    /// When a request arrives, the <paramref name="handler"/> is invoked and its
    /// return value is published as the correlated response.
    /// </summary>
    /// <typeparam name="TMessage">The incoming request message type.</typeparam>
    /// <typeparam name="TResponse">The response type to send back.</typeparam>
    /// <param name="topic">The topic to listen on (e.g. a queue name).</param>
    /// <param name="handler">
    /// Async function that processes the request and returns a response. Receives a
    /// <see cref="CancellationToken"/> scoped to the individual request (its deadline where the
    /// implementation enforces one, plus the subscription's own <paramref name="ct"/>) so a
    /// long-running handler can observe cancellation rather than run unbounded.
    /// </param>
    /// <param name="ct">Cancellation token for the subscription itself.</param>
    Task SubscribeAsync<TMessage, TResponse>(
        string topic,
        Func<TMessage, CancellationToken, Task<TResponse>> handler,
        CancellationToken ct = default)
        where TMessage : class
        where TResponse : class;
}
