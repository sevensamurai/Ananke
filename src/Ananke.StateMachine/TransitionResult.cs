namespace Ananke.StateMachine;

/// <summary>
/// Result of a state machine transition attempt.
/// Provides detailed information about the transition outcome.
/// </summary>
/// <typeparam name="S">State enum type</typeparam>
public record TransitionResult<S> where S : Enum
{
    /// <summary>
    /// Whether the transition was successful
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// The state before the transition attempt
    /// </summary>
    public required S PreviousState { get; init; }

    /// <summary>
    /// The current state after the transition attempt
    /// </summary>
    public required S CurrentState { get; init; }

    /// <summary>
    /// Error message if the transition failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Exception if one occurred during transition
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Whether this was a self-transition (same state)
    /// </summary>
    public bool IsSelfTransition => EqualityComparer<S>.Default.Equals(PreviousState, CurrentState);

    /// <summary>
    /// Whether this transition pushed the previous state onto the interrupt stack.
    /// </summary>
    public bool WasInterrupt { get; init; }

    /// <summary>
    /// Whether this transition popped a state from the interrupt stack.
    /// </summary>
    public bool WasResume { get; init; }

    /// <summary>
    /// When <see cref="WasResume"/> is <c>true</c>, the interrupt state that was exited.
    /// </summary>
    public S? ResumedFromState { get; init; }

    /// <summary>
    /// Optional payload supplied with an interrupt transition.
    /// Populated when <see cref="WasInterrupt"/> is <c>true</c> and a payload was provided.
    /// </summary>
    public object? InterruptPayload { get; init; }

    /// <summary>
    /// Creates a successful transition result
    /// </summary>
    public static TransitionResult<S> Succeeded(S previousState, S currentState) => new()
    {
        Success = true,
        PreviousState = previousState,
        CurrentState = currentState
    };

    /// <summary>
    /// Creates a failed transition result
    /// </summary>
    public static TransitionResult<S> Failed(S currentState, string errorMessage, Exception? exception = null) => new()
    {
        Success = false,
        PreviousState = currentState,
        CurrentState = currentState,
        ErrorMessage = errorMessage,
        Exception = exception
    };

    /// <summary>
    /// Creates a result for when lock acquisition failed
    /// </summary>
    public static TransitionResult<S> LockFailed(S currentState) => new()
    {
        Success = false,
        PreviousState = currentState,
        CurrentState = currentState,
        ErrorMessage = "Failed to acquire distributed lock"
    };

    /// <summary>
    /// Creates a result for invalid transition
    /// </summary>
    public static TransitionResult<S> InvalidTransition(S currentState, string transition) => new()
    {
        Success = false,
        PreviousState = currentState,
        CurrentState = currentState,
        ErrorMessage = $"Invalid transition '{transition}' from state '{currentState}'"
    };

    /// <summary>
    /// Creates a result for guard condition failure
    /// </summary>
    public static TransitionResult<S> GuardFailed(S currentState, string? reason = null) => new()
    {
        Success = false,
        PreviousState = currentState,
        CurrentState = currentState,
        ErrorMessage = reason ?? "Transition guard condition not met"
    };
}
