namespace Ananke.StateMachine.Config;

/// <summary>
/// Options for configuring state machine DI registration.
/// Infrastructure backends (Redis, MQTT) are registered separately
/// via their own packages (<c>Ananke.Redis</c>, <c>Ananke.MQTT</c>).
/// </summary>
public class StateMachineServiceOptions
{
    /// <summary>
    /// State machine runtime behavior options.
    /// </summary>
    public StateMachineOptions StateMachineOptions { get; set; } = new();

    /// <summary>
    /// Allow implicit self-transitions (state to same state).
    /// </summary>
    public StateMachineServiceOptions AllowImplicitSelfTransitions(bool allow = true)
    {
        StateMachineOptions.AllowImplicitSelfTransitions = allow;
        return this;
    }

    /// <summary>
    /// Configure lock retry behavior.
    /// </summary>
    public StateMachineServiceOptions ConfigureLockRetry(int maxRetries = 3, int retryDelayMs = 100)
    {
        StateMachineOptions.LockRetryCount = maxRetries;
        StateMachineOptions.LockRetryDelayMs = retryDelayMs;
        return this;
    }
}
