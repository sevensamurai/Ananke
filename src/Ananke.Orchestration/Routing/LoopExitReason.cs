namespace Ananke.Orchestration.Routing;

/// <summary>
/// Indicates why a loop terminated.
/// </summary>
public enum LoopExitReason
{
    /// <summary>The <c>Until</c> predicate returned <c>true</c>.</summary>
    ConditionMet,

    /// <summary>The maximum iteration count was reached.</summary>
    MaxIterationsReached
}
