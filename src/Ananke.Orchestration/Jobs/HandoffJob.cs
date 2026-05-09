using Ananke.Orchestration.Workflows;
using Ananke.Abstractions.Channels;
using Ananke.Orchestration.Tracing;

namespace Ananke.Orchestration.Jobs;

/// <summary>
/// A job that delegates work to an external agent or service via an <see cref="IHandoffChannel"/>.
/// Publishes a message derived from the current workflow state and awaits a correlated response,
/// then maps the response back into the state.
/// </summary>
/// <remarks>
/// Create instances via <see cref="Handoff.To{TState, TMessage, TResponse}"/> for fluent syntax.
/// The correlation ID is derived from the ambient <see cref="WorkflowTraceContext"/> when available
/// (format: <c>{executionId}/{jobName}</c>), falling back to a new GUID.
/// </remarks>
/// <typeparam name="TState">Workflow state type.</typeparam>
/// <typeparam name="TMessage">The outgoing message type published to the channel.</typeparam>
/// <typeparam name="TResponse">The response type received from the external agent.</typeparam>
public sealed class HandoffJob<TState, TMessage, TResponse> : IJob<TState>
    where TMessage : class
    where TResponse : class
{
    private readonly string _topic;
    private readonly IHandoffChannel _channel;
    private readonly Func<TState, TMessage> _createMessage;
    private readonly Func<TState, TResponse, TState> _mapResult;
    private readonly TimeSpan _timeout;

    internal HandoffJob(
        string name,
        string topic,
        IHandoffChannel channel,
        Func<TState, TMessage> createMessage,
        Func<TState, TResponse, TState> mapResult,
        TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(createMessage);
        ArgumentNullException.ThrowIfNull(mapResult);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        Name = name;
        _topic = topic;
        _channel = channel;
        _createMessage = createMessage;
        _mapResult = mapResult;
        _timeout = timeout;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public async Task<TState> ExecuteAsync(TState state, CancellationToken ct = default)
    {
        var correlationId = WorkflowTraceContext.Value is { } trace
            ? $"{trace.ExecutionId}/{trace.CurrentJob}"
            : Guid.NewGuid().ToString("N");

        var message = _createMessage(state);

        TResponse response;
        try
        {
            response = await _channel.SendAsync<TMessage, TResponse>(
                _topic, correlationId, message, _timeout, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Handoff to '{_topic}' (correlation: {correlationId}) timed out after {_timeout.TotalSeconds:F1}s.");
        }

        return _mapResult(state, response);
    }
}
