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
    /// Delay between lock retry attempts in milliseconds. Default is 100.
    /// </summary>
    public int LockRetryDelayMs { get; set; } = 100;
}
