using System.Collections.Concurrent;
using Ananke.Abstractions.Channels;

namespace Ananke.Orchestration.Jobs;

/// <summary>
/// In-memory <see cref="IHandoffChannel"/> for testing handoff workflows without a message broker.
/// </summary>
/// <remarks>
/// <para>Supports two usage patterns:</para>
/// <list type="bullet">
///   <item>
///     <b>Auto-respond:</b> Register a handler via
///     <see cref="RegisterHandler{TMessage, TResponse}(string, Func{TMessage, TResponse})"/>
///     before running the workflow. The handler is invoked when the handoff occurs.
///   </item>
///   <item>
///     <b>Manual respond:</b> Call <see cref="CompleteAsync{TResponse}"/> from test code
///     after the workflow has started and is waiting on the handoff.
///   </item>
/// </list>
/// </remarks>
public sealed class InMemoryHandoffChannel : IHandoffChannel
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<object>> _pending = new();
    private readonly ConcurrentDictionary<string, Func<object, Task<object>>> _handlers = new();

    /// <summary>
    /// Registers a synchronous auto-response handler for a given topic.
    /// When <see cref="SendAsync{TMessage, TResponse}"/> is called for this topic,
    /// the handler runs immediately and its return value becomes the response.
    /// </summary>
    public void RegisterHandler<TMessage, TResponse>(
        string topic,
        Func<TMessage, TResponse> handler)
        where TMessage : class
        where TResponse : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(handler);

        _handlers[topic] = msg => Task.FromResult<object>(handler((TMessage)msg)!);
    }

    /// <summary>
    /// Registers an asynchronous auto-response handler for a given topic.
    /// </summary>
    public void RegisterHandler<TMessage, TResponse>(
        string topic,
        Func<TMessage, Task<TResponse>> handler)
        where TMessage : class
        where TResponse : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(handler);

        _handlers[topic] = async msg =>
        {
            var result = await handler((TMessage)msg);
            return result!;
        };
    }

    /// <inheritdoc />
    public async Task<TResponse> SendAsync<TMessage, TResponse>(
        string topic,
        string correlationId,
        TMessage message,
        TimeSpan timeout,
        CancellationToken ct = default)
        where TMessage : class
        where TResponse : class
    {
        if (_handlers.TryGetValue(topic, out var handler))
        {
            var result = await handler(message);
            return (TResponse)result;
        }

        var key = $"{topic}/{correlationId}";
        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[key] = tcs;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        await using var reg = cts.Token.Register(() => tcs.TrySetCanceled(cts.Token));

        try
        {
            var result = await tcs.Task;
            return (TResponse)result;
        }
        finally
        {
            _pending.TryRemove(key, out _);
        }
    }

    /// <inheritdoc />
    public Task CompleteAsync<TResponse>(
        string topic,
        string correlationId,
        TResponse response,
        CancellationToken ct = default)
        where TResponse : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        ArgumentNullException.ThrowIfNull(response);

        var key = $"{topic}/{correlationId}";
        if (_pending.TryRemove(key, out var tcs))
            tcs.TrySetResult(response);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SubscribeAsync<TMessage, TResponse>(
        string topic,
        Func<TMessage, Task<TResponse>> handler,
        CancellationToken ct = default)
        where TMessage : class
        where TResponse : class
    {
        RegisterHandler(topic, handler);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => default;
}
