namespace Ananke.Orchestration.Routing;

/// <summary>
/// What a join-time merge callback sees: the states of branches that succeeded, plus the outcome
/// of <b>every</b> branch in the fork — including those that did not.
/// </summary>
/// <remarks>
/// <para>
/// The merge callback is the coordinator: it is the one place that runs after every branch has
/// terminated but before the workflow continues, so it is where a caller decides what a partial
/// fork result means — substitute a default, accept the partial merge, or throw. Before this
/// existed the callback received only <see cref="States"/> and could not tell three successes
/// from two successes plus a dropped branch.
/// </para>
/// </remarks>
/// <typeparam name="TState">The workflow state type.</typeparam>
public sealed record JoinContext<TState>
{
    /// <summary>
    /// States from branches that succeeded, in join-source order. Shorter than
    /// <see cref="Outcomes"/> whenever a branch did not succeed.
    /// </summary>
    public required IReadOnlyList<TState> States { get; init; }

    /// <summary>Outcome of every branch in the fork, in fork-target order.</summary>
    public required IReadOnlyList<BranchOutcome> Outcomes { get; init; }

    /// <summary><see langword="true"/> when at least one branch did not succeed.</summary>
    public bool HasFailures => Outcomes.Any(o => !o.Succeeded);
}
