namespace Ananke.Orchestration.Routing;

/// <summary>
/// Controls how a fork handles branch failures.
/// </summary>
public enum ForkMode
{
    /// <summary>If any branch faults, cancel all siblings and propagate the exception.</summary>
    FailFast,

    /// <summary>Continue executing remaining branches even if one faults. Only fail if all branches fault.</summary>
    BestEffort
}
