namespace Ananke.StateMachine.Builder;

/// <summary>
/// Fluent builder interface for configuring state machine transitions
/// </summary>
/// <typeparam name="S">State enum type</typeparam>
/// <typeparam name="T">Transition enum type</typeparam>
public interface ITransitionBuilder<S, T>
    where S : Enum
    where T : Enum
{
    /// <summary>
    /// Starts defining a transition from the specified state
    /// </summary>
    IFromStateBuilder<S, T> From(S state);

    /// <summary>
    /// Starts defining a transition from any of the specified states
    /// </summary>
    IFromStateBuilder<S, T> FromAny(params S[] states);

    /// <summary>
    /// Configures a specific state with entry/exit actions
    /// </summary>
    IStateConfigBuilder<S, T> State(S state);
}

/// <summary>
/// Builder for configuring what transition triggers from a state
/// </summary>
public interface IFromStateBuilder<S, T>
    where S : Enum
    where T : Enum
{
    /// <summary>
    /// Specifies the transition that triggers this state change
    /// </summary>
    IToStateBuilder<S, T> On(T transition);
}

/// <summary>
/// Builder for configuring the target state of a transition
/// </summary>
public interface IToStateBuilder<S, T>
    where S : Enum
    where T : Enum
{
    /// <summary>
    /// Specifies the target state of this transition
    /// </summary>
    ITransitionConfigBuilder<S, T> To(S targetState);
}

/// <summary>
/// Builder for configuring additional transition options
/// </summary>
public interface ITransitionConfigBuilder<S, T> : ITransitionBuilder<S, T>
    where S : Enum
    where T : Enum
{
    /// <summary>
    /// Adds a guard condition that must be met for the transition to occur
    /// </summary>
    ITransitionConfigBuilder<S, T> When(Func<bool> condition);

    /// <summary>
    /// Adds an async guard condition that must be met for the transition to occur
    /// </summary>
    ITransitionConfigBuilder<S, T> WhenAsync(Func<Task<bool>> condition);

    /// <summary>
    /// Adds an action to execute after the transition completes
    /// </summary>
    ITransitionConfigBuilder<S, T> WithAction(Func<Task> action);

    /// <summary>
    /// Adds an action to execute after the transition that can modify the final state
    /// </summary>
    ITransitionConfigBuilder<S, T> WithAction(Func<Task<S>> action);
}

/// <summary>
/// Builder for configuring state-specific behaviors
/// </summary>
public interface IStateConfigBuilder<S, T> : ITransitionBuilder<S, T>
    where S : Enum
    where T : Enum
{
    /// <summary>
    /// Action to execute when entering this state
    /// </summary>
    IStateConfigBuilder<S, T> OnEnter(Func<Task> action);

    /// <summary>
    /// Action to execute when exiting this state
    /// </summary>
    IStateConfigBuilder<S, T> OnExit(Func<Task> action);
}

/// <summary>
/// Configuration for a single transition
/// </summary>
public class TransitionConfig<S, T>
    where S : Enum
    where T : Enum
{
    public required S InitialState { get; init; }
    public required T Transition { get; init; }
    public required S FinalState { get; init; }
    public Func<Task<bool>>? GuardCondition { get; set; }
    public Func<Task<S>>? AfterTransitionAction { get; set; }
}

/// <summary>
/// Configuration for state entry/exit actions
/// </summary>
public class StateConfig<S>
    where S : Enum
{
    public required S State { get; init; }
    public Func<Task>? OnEnterAction { get; set; }
    public Func<Task>? OnExitAction { get; set; }
}
