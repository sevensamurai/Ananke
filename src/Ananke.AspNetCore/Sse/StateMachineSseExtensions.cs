using Ananke.StateMachine;

namespace Ananke.AspNetCore.Sse;

/// <summary>
/// Extension methods for running a state-machine–driven SSE loop over an HTTP response.
/// </summary>
public static class StateMachineSseExtensions
{
    /// <summary>
    /// Awaits the state machine's <see cref="StateMachine{S,T}.CurrentWork"/> in a loop,
    /// surviving interrupt-induced cancellation. During transitions the state machine
    /// exposes a bridge task that stays pending until the new state's work starts,
    /// so there is never a <see langword="null"/> gap visible to this loop.
    /// <para>
    /// Returns <see langword="true"/> if the machine reached <paramref name="terminalState"/>;
    /// <see langword="false"/> if the machine became idle (no more work) without reaching it.
    /// </para>
    /// </summary>
    /// <typeparam name="S">State enum type.</typeparam>
    /// <typeparam name="T">Transition enum type.</typeparam>
    /// <param name="machine">The state machine to observe.</param>
    /// <param name="terminalState">The state that signals the loop should stop.</param>
    /// <param name="ct">
    /// Abandons the loop when the client disconnects. Pass <c>HttpContext.RequestAborted</c>.
    /// <para>
    /// Cancelling this <b>propagates</b>, rather than returning <see langword="false"/>: a
    /// disconnected client is not the same outcome as "the machine went idle before reaching the
    /// terminal state", and collapsing the two would report a normal result for an abandoned
    /// request. The state machine's own work is left running — it owns that lifetime.
    /// </para>
    /// </param>
    public static async Task<bool> RunSseLoopAsync<S, T>(
        this StateMachine<S, T> machine,
        S terminalState,
        CancellationToken ct = default)
        where S : Enum
        where T : Enum
    {
        while (!EqualityComparer<S>.Default.Equals(machine.CurrentState, terminalState))
        {
            ct.ThrowIfCancellationRequested();

            var work = machine.CurrentWork;
            if (work is null) break;

            // The filter is what separates the two cancellations that can land here: the machine
            // cancelling its own state work on an interrupt (expected — keep looping), versus this
            // caller's token firing (the request is gone — let it propagate).
            try { await work.WaitAsync(ct); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }

            // The task we just awaited completed (or was cancelled).
            // If a transition started new work, CurrentWork differs — keep looping.
            // Otherwise this chat turn is done.
            if (machine.CurrentWork == work) break;
        }

        return EqualityComparer<S>.Default.Equals(machine.CurrentState, terminalState);
    }
}
