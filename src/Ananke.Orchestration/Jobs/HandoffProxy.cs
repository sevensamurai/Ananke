using Ananke.Orchestration.Workflows;
using Ananke.Abstractions.Channels;
using Ananke.Orchestration.Tracing;

namespace Ananke.Orchestration.Jobs;

/// <summary>
/// Typed, reusable client for sending handoff requests to an external service.
/// Wraps <see cref="IHandoffChannel"/> with a fixed topic, timeout, and automatic
/// correlation ID generation — removing transport plumbing from application code.
/// </summary>
/// <remarks>
/// Create instances via <see cref="Handoff.Proxy{TMessage, TResponse}"/>.
/// For workflow-level handoffs that map into workflow state, use
/// <see cref="HandoffJob{TState, TMessage, TResponse}"/> instead.
/// </remarks>
/// <typeparam name="TMessage">The outgoing message type published to the channel.</typeparam>
/// <typeparam name="TResponse">The response type received from the external service.</typeparam>
public sealed class HandoffProxy<TMessage, TResponse>
    where TMessage : class
    where TResponse : class
{
    private readonly string _topic;
    private readonly IHandoffChannel _channel;
    private readonly TimeSpan _timeout;

    internal HandoffProxy(string topic, IHandoffChannel channel, TimeSpan timeout)
    {
        _topic = topic;
        _channel = channel;
        _timeout = timeout;
    }

    /// <summary>The destination topic this proxy targets.</summary>
    public string Topic => _topic;

    /// <summary>
    /// Sends a message to the configured topic and awaits a correlated response.
    /// The correlation ID is derived from the ambient <see cref="WorkflowTraceContext"/>
    /// when available, otherwise a new GUID is generated.
    /// </summary>
    /// <param name="message">The message payload to send.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The deserialized response from the external service.</returns>
    /// <exception cref="TimeoutException">
    /// Thrown when no response is received within the configured timeout.
    /// </exception>
    public async Task<TResponse> SendAsync(TMessage message, CancellationToken ct = default)
    {
        var correlationId = WorkflowTraceContext.Value is { } trace
            ? $"{trace.ExecutionId}/{trace.CurrentJob}"
            : Guid.NewGuid().ToString("N");

        try
        {
            return await _channel.SendAsync<TMessage, TResponse>(
                _topic, correlationId, message, _timeout, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Handoff to '{_topic}' (correlation: {correlationId}) timed out after {_timeout.TotalSeconds:F1}s.");
        }
    }
}
