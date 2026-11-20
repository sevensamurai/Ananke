namespace Ananke.StateMachine;

/// <summary>
/// Options for configuring state machine behavior
/// </summary>
public class StateMachineOptions
{
    /// <summary>
    /// When true, transitions from a state to itself are implicitly allowed
    /// without needing to explicitly define them. Default is true.
    /// </summary>
    public bool AllowImplicitSelfTransitions { get; set; } = true;

    /// <summary>
    /// Maximum retries for lock acquisition. Default is 3.
    /// </summary>
    public int LockRetryCount { get; set; } = 3;

    /// <summary>
    /// Delay before the first lock retry in milliseconds; it doubles before each further retry. Default is 100.
    /// </summary>
    public int LockRetryDelayMs { get; set; } = 100;

    /// <summary>
    /// Maximum depth of nested interrupt transitions. Exceeding this rejects the transition. Default is 5.
    /// </summary>
    public int MaxInterruptDepth { get; set; } = 5;

    /// <summary>
    /// Clock that stamps a transition whose payload carries no event time. Default is
    /// <see cref="TimeProvider.System"/>.
    /// </summary>
    public TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>
    /// First part of each context's store key and lock resource:
    /// <c>{KeyPrefix}:{MachineName}:{context id}</c>. Default is <c>"statemachine"</c>; empty
    /// leaves it out.
    /// </summary>
    public string KeyPrefix { get; set; } = "statemachine";

    /// <summary>
    /// Middle part of each context's store key and lock resource. <c>null</c> (the default) uses the
    /// machine's type name with its type arguments, so two machine types keep separate records for
    /// the same context id. Set it when two types in different namespaces share a name, or to keep
    /// the keys when the type is renamed. Empty leaves it out: with <see cref="KeyPrefix"/> empty
    /// too, the key is the bare context id.
    /// </summary>
    public string? MachineName { get; set; }
}
