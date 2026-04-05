using Ananke.Abstractions;

namespace Ananke.StateMachine.Channels;

/// <summary>
/// Represents a completed transition event, carrying the context, the transition
/// that was attempted, and the outcome. Used as the work item for post-transition
/// background processing via <see cref="StateMachineChannelWorker{C, S, T, N}"/>.
/// </summary>
/// <typeparam name="C">Context type implementing <see cref="IBaseContext"/>.</typeparam>
/// <typeparam name="S">State enum type.</typeparam>
/// <typeparam name="T">Transition enum type.</typeparam>
public sealed record TransitionEvent<C, S, T>
    where C : IBaseContext
    where S : Enum
    where T : Enum
{
    /// <summary>The context that triggered the transition.</summary>
    public required C Context { get; init; }

    /// <summary>The transition that was attempted.</summary>
    public required T Transition { get; init; }

    /// <summary>The outcome of the transition attempt.</summary>
    public required TransitionResult<S> Result { get; init; }
}
