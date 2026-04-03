using Ananke.Abstractions;

namespace Ananke.StateMachine;

/// <summary>
/// Operational status of a state machine instance.
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
/// Core state machine interface for distributed state management.
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
    /// Transitions the state machine to a new state.
    /// Returns a result indicating success/failure and state information.
    /// Blocked when OperationalStatus is Faulted.
    /// </summary>
    /// <param name="context">The context identifying the state machine instance</param>
    /// <param name="transition">The transition to execute</param>
    /// <returns>Result containing transition outcome and state information</returns>
    Task<TransitionResult<S>> TransitionAsync(C context, T transition);

    /// <summary>
    /// Transitions the state machine to a new state, carrying an optional payload.
    /// For interrupt transitions, the payload is stored alongside the interrupt stack
    /// and surfaced in <see cref="TransitionResult{S}.InterruptPayload"/>.
    /// </summary>
    /// <param name="context">The context identifying the state machine instance</param>
    /// <param name="transition">The transition to execute</param>
    /// <param name="payload">Arbitrary data to associate with the transition (e.g. an interrupt reason)</param>
    /// <returns>Result containing transition outcome and state information</returns>
    Task<TransitionResult<S>> TransitionAsync(C context, T transition, object? payload);

    /// <summary>
    /// Sends a notification without changing state.
    /// Use for events that don't affect state but need to be broadcast.
    /// </summary>
    /// <param name="context">The context identifying the state machine instance</param>
    /// <param name="notification">The notification to send</param>
    Task NotifyAsync(C context, N notification);

    /// <summary>
    /// Gets the current state of the state machine
    /// </summary>
    S CurrentState { get; }
    
    /// <summary>
    /// Gets the current operational status (Operative/Faulted)
    /// </summary>
    OperationalStatus OperationalStatus { get; }
    
    /// <summary>
    /// Reason for current status (populated when Faulted)
    /// </summary>
    string? OperationalStatusReason { get; }

    /// <summary>
    /// <c>true</c> while the interrupt stack is non-empty — i.e. the machine is servicing
    /// an interrupt and can later resume to the prior state via a <c>ToResume</c> transition.
    /// </summary>
    bool IsInterrupted { get; }
    
    /// <summary>
    /// Marks the state machine as Faulted. Blocks all transitions until Reset.
    /// </summary>
    /// <param name="context">Context identifying the instance</param>
    /// <param name="reason">Reason for the fault</param>
    /// <returns>Status change result</returns>
    Task<OperationalStatusChange> FaultAsync(C context, string reason);
    
    /// <summary>
    /// Resets the state machine from Faulted to Operative.
    /// </summary>
    /// <param name="context">Context identifying the instance</param>
    /// <param name="reason">Reason for the reset (e.g., "Device replaced")</param>
    /// <returns>Status change result</returns>
    Task<OperationalStatusChange> ResetAsync(C context, string reason);
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
    Task<S> GetStateAsync(string contextId);

    /// <summary>
    /// Checks if a transition is valid from the current state
    /// </summary>
    bool CanTransition(S currentState, T transition);
}