namespace Ananke.Abstractions.Channels;

/// <summary>
/// Request-response messaging abstraction for agent-to-agent handoff.
/// The sender publishes a message and awaits a correlated response; the responder
/// completes the pending request via <see cref="CompleteAsync{TResponse}"/>.
/// </summary>
/// <remarks>
/// Implementations: <c>InMemoryHandoffChannel</c> (testing, in Ananke.Orchestration) and
/// <c>MqttHandoffChannel</c> (production, in Ananke.MQTT).
/// </remarks>
public interface IHandoffChannel
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
}
