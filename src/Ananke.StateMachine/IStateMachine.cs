using Ananke.Abstractions;

namespace Ananke.StateMachine;

/// <summary>
/// Operational status of one context of a state machine.
/// Acts as a supervisory dimension over normal state transitions.
/// </summary>
public enum OperationalStatus
{
    /// <summary>
    /// Normal operation - transitions are processed
    /// </summary>
    Operative,

    /// <summary>
    /// Faulted - transitions are blocked, requires manual reset
    /// </summary>
    Faulted
}

/// <summary>
/// Result of an operational status change
/// </summary>
/// <param name="Success">Whether the status change succeeded</param>
/// <param name="PreviousStatus">Status before the change</param>
/// <param name="CurrentStatus">Status after the change</param>
/// <param name="Reason">Reason for the status change</param>
public readonly record struct OperationalStatusChange(
    bool Success,
    OperationalStatus PreviousStatus,
    OperationalStatus CurrentStatus,
    string Reason);

/// <summary>
/// A context's stored state and operational status, read by
/// <see cref="IActionStateMachine{C,S,T,N}.GetStatusAsync"/>.
/// </summary>
/// <typeparam name="S">State enum type</typeparam>
/// <param name="State">The context's current state</param>
/// <param name="Step">How many transitions the context has made</param>
/// <param name="OperationalStatus">Operative, or Faulted until reset</param>
/// <param name="OperationalStatusReason">Reason for the status (populated when Faulted)</param>
/// <param name="IsInterrupted">
/// <c>true</c> while the context's interrupt stack is non-empty, i.e. it can later resume to the
/// prior state via a <c>ToResume</c> transition.
/// </param>
public readonly record struct ContextStatus<S>(
    S State,
    int Step,
    OperationalStatus OperationalStatus,
    string? OperationalStatusReason,
    bool IsInterrupted)
    where S : Enum;

/// <summary>
/// Core state machine interface for distributed state management. One machine serves many
/// contexts: state and operational status belong to each context and are read with
/// <see cref="GetStatusAsync"/>.
/// </summary>
/// <typeparam name="C">Context type implementing IBaseContext</typeparam>
/// <typeparam name="S">State enum type</typeparam>
/// <typeparam name="T">Transition enum type</typeparam>
/// <typeparam name="N">Notification enum type</typeparam>
public interface IActionStateMachine<C, S, T, N>
    where C : IBaseContext
    where S : Enum
    where T : Enum
    where N : Enum
{
    /// <summary>
    /// Transitions a context to a new state.
    /// Returns a result indicating success/failure and state information.
    /// Refused when the context is Faulted.
    /// </summary>
    /// <param name="context">The context to transition</param>
    /// <param name="transition">The transition to execute</param>
    /// <param name="ct">Stops the transition with <see cref="OperationCanceledException"/>, up to its exit action</param>
    /// <returns>Result containing transition outcome and state information</returns>
    Task<TransitionResult<S>> TransitionAsync(C context, T transition, CancellationToken ct = default);

    /// <summary>
    /// Transitions a context to a new state, carrying an optional payload.
    /// For interrupt transitions, the payload is stored alongside the interrupt stack
    /// and surfaced in <see cref="TransitionResult{S}.InterruptPayload"/>.
    /// When the payload implements <see cref="Ananke.Abstractions.ITimestamped"/> its
    /// <see cref="Ananke.Abstractions.ITimestamped.EventTime"/> is used as the attributed
    /// event timestamp on <see cref="TransitionResult{S}.EventTimestamp"/>; otherwise the time
    /// from <see cref="StateMachineOptions.Clock"/> is used.
    /// </summary>
    /// <param name="context">The context to transition</param>
    /// <param name="transition">The transition to execute</param>
    /// <param name="payload">Arbitrary data to associate with the transition (e.g. an interrupt reason)</param>
    /// <param name="ct">Stops the transition with <see cref="OperationCanceledException"/>, up to its exit action</param>
    /// <returns>Result containing transition outcome and state information</returns>
    Task<TransitionResult<S>> TransitionAsync(C context, T transition, object? payload, CancellationToken ct = default);

    /// <summary>
    /// Sends a notification without changing state.
    /// Use for events that don't affect state but need to be broadcast.
    /// </summary>
    /// <param name="context">The context the notification is about</param>
    /// <param name="notification">The notification to send</param>
    /// <param name="ct">Cancels sending</param>
    Task NotifyAsync(C context, N notification, CancellationToken ct = default);

    /// <summary>
    /// Reads a context's stored state and operational status. A context with no record is at the
    /// machine's initial state and Operative. Throws when the store cannot be read.
    /// </summary>
    /// <param name="context">The context to read</param>
    /// <param name="ct">Cancels the store read</param>
    Task<ContextStatus<S>> GetStatusAsync(C context, CancellationToken ct = default);

    /// <summary>
    /// Marks one context as Faulted, on every node that shares the store. Its transitions are
    /// refused until <see cref="ResetAsync"/>; other contexts are not affected.
    /// Throws when the context's lock cannot be acquired or the store cannot be read or written;
    /// the status is then unchanged.
    /// </summary>
    /// <param name="context">The context to fault</param>
    /// <param name="reason">Reason for the fault</param>
    /// <param name="ct">Cancels waiting for the lock and the store calls.</param>
    /// <returns>Status change result</returns>
    Task<OperationalStatusChange> FaultAsync(C context, string reason, CancellationToken ct = default);

    /// <summary>
    /// Resets one context from Faulted to Operative.
    /// Throws when the context's lock cannot be acquired or the store cannot be read or written;
    /// the status is then unchanged.
    /// </summary>
    /// <param name="context">The context to reset</param>
    /// <param name="reason">Reason for the reset (e.g., "Device replaced")</param>
    /// <param name="ct">Cancels waiting for the lock and the store calls.</param>
    /// <returns>Status change result</returns>
    Task<OperationalStatusChange> ResetAsync(C context, string reason, CancellationToken ct = default);
}

/// <summary>
/// Extended state machine interface with state query capabilities
/// </summary>
public interface IQueryableStateMachine<C, S, T, N> : IActionStateMachine<C, S, T, N>
    where C : IBaseContext
    where S : Enum
    where T : Enum
    where N : Enum
{
    /// <summary>
    /// Gets the current state for a specific context
    /// </summary>
    Task<S> GetStateAsync(string contextId, CancellationToken ct = default);

    /// <summary>
    /// Checks if a transition is valid from the current state
    /// </summary>
    bool CanTransition(S currentState, T transition);
}
