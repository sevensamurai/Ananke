using Ananke.Abstractions;
using Ananke.Abstractions.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ananke.StateMachine.Channels;

/// <summary>
/// Bridge between an <see cref="IChannelReader{M, A}"/> and an
/// <see cref="IActionStateMachine{C, S, T, N}"/>.
/// Receives <c>(context, transition)</c> pairs from the channel and dispatches
/// them to the state machine — eliminating the need for hand-written
/// <see cref="IBackgroundWorker{T}"/> implementations.
/// </summary>
/// <remarks>
/// <para>
/// This is the pub/sub equivalent of <c>HandoffJob</c>/<c>HandoffProxy</c> in the
/// request-response channel family. Plug it into any <see cref="IChannelReader{M, A}"/>:
/// </para>
/// <code>
/// var worker = new StateMachineChannelWorker&lt;Ctx, State, Trans, Notif&gt;(machine);
/// await reader.ConfigureAsync(config, worker);
/// </code>
/// <para>
/// An optional <see cref="OnTransition"/> callback is invoked after each successful
/// or failed transition, enabling logging, metrics, or publishing results back
/// to another channel.
/// </para>
/// </remarks>
/// <typeparam name="C">Context type implementing <see cref="IBaseContext"/>.</typeparam>
/// <typeparam name="S">State enum type.</typeparam>
/// <typeparam name="T">Transition enum type (matches the channel's action enum).</typeparam>
/// <typeparam name="N">Notification enum type.</typeparam>
public sealed class StateMachineChannelWorker<C, S, T, N>(
    IActionStateMachine<C, S, T, N> machine,
    IBackgroundWorker<TransitionEvent<C, S, T>>? postTransitionWorker = null,
    ILogger<StateMachineChannelWorker<C, S, T, N>>? logger = null)
    : IBackgroundWorker<C, T>, IAsyncDisposable
    where C : IBaseContext
    where S : Enum
    where T : Enum
    where N : Enum
{
    private readonly ILogger<StateMachineChannelWorker<C, S, T, N>> _logger =
        logger ?? NullLogger<StateMachineChannelWorker<C, S, T, N>>.Instance;

    private readonly BackgroundProcessor<TransitionEvent<C, S, T>>? _postProcessor =
        postTransitionWorker is not null
            ? StartProcessor(postTransitionWorker, logger ?? NullLogger<StateMachineChannelWorker<C, S, T, N>>.Instance)
            : null;

    /// <summary>
    /// Optional synchronous callback invoked inline after each transition attempt.
    /// Suitable for fast, non-blocking work such as logging or metrics.
    /// </summary>
    public Action<C, T, TransitionResult<S>>? OnTransition { get; init; }

    /// <inheritdoc />
    public async Task HandleAsync(C? item, T action, CancellationToken token)
    {
        if (item is null)
        {
            _logger.LogDebug("Received null context — skipping");
            return;
        }

        _logger.LogDebug("Dispatching [{Transition}] for context {ContextId}",
            action, item.Id);

        var result = await machine.TransitionAsync(item, action);

        if (result.Success)
        {
            _logger.LogDebug("Transition [{Transition}] succeeded: {PreviousState} → {CurrentState} for {ContextId}",
                action, result.PreviousState, result.CurrentState, item.Id);
        }
        else
        {
            _logger.LogWarning("Transition [{Transition}] blocked for {ContextId}: {Error}",
                action, item.Id, result.ErrorMessage);
        }

        OnTransition?.Invoke(item, action, result);

        if (_postProcessor is not null)
        {
            var evt = new TransitionEvent<C, S, T>
            {
                Context = item,
                Transition = action,
                Result = result
            };

            await _postProcessor.EnqueueAsync(evt, token);
        }
    }

    private static BackgroundProcessor<TransitionEvent<C, S, T>> StartProcessor(
        IBackgroundWorker<TransitionEvent<C, S, T>> worker,
        ILogger logger)
    {
        var processor = new BackgroundProcessor<TransitionEvent<C, S, T>>(
            worker,
            onError: (ex, evt) => logger.LogError(ex,
                "Post-transition worker failed for {ContextId} [{Transition}]",
                evt?.Context.Id, evt is not null ? evt.Transition : default));
        processor.Start();
        return processor;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_postProcessor is not null)
            await _postProcessor.DisposeAsync();
    }
}
