namespace Ananke.Abstractions;

/// <summary>
/// Passed to the configure callback of the one-call <c>AddStateMachine</c> overload in Ananke.StateMachine.
/// Infrastructure packages add their own registrations through extension methods on it
/// (<c>UseRedis</c> in Ananke.Redis, <c>UseMqtt</c> in Ananke.MQTT); whatever they leave unregistered
/// stays in memory.
/// </summary>
/// <typeparam name="TServices">
/// The service collection type, <c>IServiceCollection</c> as <c>AddStateMachine</c> gives it. A type parameter
/// so that this package needs no dependency injection package.
/// </typeparam>
/// <typeparam name="TContext">Context type of the state machine; the message type of its channels.</typeparam>
/// <typeparam name="TTransition">Transition enum type.</typeparam>
/// <typeparam name="TNotification">Notification enum type.</typeparam>
public sealed class StateMachineBuilder<TServices, TContext, TTransition, TNotification>(TServices services)
    where TServices : class
    where TContext : class, IBaseContext
    where TTransition : Enum
    where TNotification : Enum
{
    /// <summary>The service collection the state machine is registered in.</summary>
    public TServices Services { get; } = services;
}
