namespace Ananke.Orchestration.Routing;

/// <summary>
/// Describes the targets of a fork operation. Created via <see cref="Workflow.Fork(string[])"/>
/// and passed to <see cref="Workflow{TState}.Then(string, ForkTarget)"/>.
/// </summary>
public sealed class ForkTarget
{
    /// <summary>The job names to execute in parallel.</summary>
    public IReadOnlyList<string> Targets { get; }

    /// <summary>Controls branch cancellation behavior when a branch faults.</summary>
    public ForkMode Mode { get; }

    internal ForkTarget(string[] targets, ForkMode mode = ForkMode.FailFast)
    {
        ArgumentNullException.ThrowIfNull(targets);

        if (targets.Length < 2)
            throw new ArgumentException("Fork requires at least two targets.", nameof(targets));

        Targets = targets;
        Mode = mode;
    }
}
