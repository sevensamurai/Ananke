namespace Ananke.Orchestration.Routing;

/// <summary>How a single forked branch terminated.</summary>
public enum BranchOutcomeKind
{
    /// <summary>The branch ran to its terminal job without faulting.</summary>
    Succeeded,

    /// <summary>A job in the branch threw. <see cref="BranchOutcome.Exception"/> holds it.</summary>
    Faulted,

    /// <summary>
    /// The branch was cancelled — typically a sibling fault under <see cref="ForkMode.FailFast"/>,
    /// which cancels the remaining branches. A cancelled branch is not a fault and is reported
    /// separately so callers do not treat it as one.
    /// </summary>
    Cancelled,

    /// <summary>
    /// The branch was stopped by a guardrail rather than by anything that went wrong — today,
    /// reaching the cost budget under <see cref="Budget.BudgetMode.Stop"/>.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Cancelled"/>, which is collateral from a sibling's failure. A
    /// stopped branch ran correctly and was ended by policy, and the work it completed before
    /// stopping is in its history. Deliberately named for the outcome rather than the cause, so
    /// a future step or duration guard reuses it instead of adding a near-identical member.
    /// </remarks>
    Stopped
}

/// <summary>
/// The terminal outcome of one forked branch. Surfaced on
/// <see cref="Workflows.WorkflowResult{TState}.BranchOutcomes"/> and to a
/// <see cref="JoinContext{TState}"/> merge callback so a caller can decide what a partial fork
/// result means.
/// </summary>
/// <remarks>
/// Before this existed, a branch that failed under <see cref="ForkMode.BestEffort"/> left no
/// program-visible trace at all: its exception was discarded unless every branch failed, its job
/// history never reached the execution, and the run reported success.
/// </remarks>
public sealed record BranchOutcome
{
    /// <summary>The job this branch was forked to — its entry point, and its identity in the fork.</summary>
    public required string BranchTarget { get; init; }

    /// <summary>
    /// The last job that completed successfully in this branch, or <see cref="BranchTarget"/>
    /// when none did.
    /// </summary>
    public required string FinalJob { get; init; }

    /// <summary>How the branch terminated.</summary>
    public required BranchOutcomeKind Kind { get; init; }

    /// <summary>
    /// The fault that ended the branch, when <see cref="Kind"/> is
    /// <see cref="BranchOutcomeKind.Faulted"/>. <see langword="null"/> otherwise.
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// <see langword="true"/> when this branch contributed a state to the join.
    /// </summary>
    public bool Succeeded => Kind == BranchOutcomeKind.Succeeded;
}
