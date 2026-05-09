using Ananke.Design;


using Ananke.Learning.EmpiricalMemory;

namespace Ananke.Organics.Division;

/// <summary>
/// Evaluates whether a workflow cell should divide and proposes a
/// <see cref="DivisionPlan"/> describing the split. Returns <see langword="null"/>
/// when no division is needed.
/// </summary>
/// <remarks>
/// <para>
/// The division trigger is <b>surface tension</b> — structural complexity metrics —
/// not failure rate. Failure rate is a health signal; surface tension is a
/// structural signal. A cell divides while it is still healthy and capable of
/// performing the division cleanly.
/// </para>
/// <para>
/// On cold start (no division experience), implementations fall back to simple
/// threshold heuristics (e.g. tool count ≥ 6 AND tag clusters ≥ 2). On warm
/// start, implementations recall division strategies from
/// <c>IEmpiricalMemory</c> and use <c>IExplorationStrategy</c> (UCB) to
/// balance exploitation of proven strategies vs. exploration of novel ones.
/// </para>
/// </remarks>
public interface IDivisionPolicy
{
    /// <summary>
    /// Evaluate whether the cell should divide. Returns <see langword="null"/>
    /// if no division is needed.
    /// </summary>
    /// <param name="snapshot">Current complexity metrics for the cell.</param>
    /// <param name="manifest">The cell's workflow manifest.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<DivisionPlan?> EvaluateAsync(
        ComplexitySnapshot snapshot,
        WorkflowManifest manifest,
        CancellationToken ct = default);

    /// <summary>
    /// Experience-aware overload. Receives recent <see cref="DivisionExperience"/>
    /// records from the same lineage so the policy can weight cluster strategies
    /// based on past outcomes. The default implementation ignores experience and
    /// delegates to the legacy overload — existing implementations compile unchanged.
    /// </summary>
    /// <param name="snapshot">Current complexity metrics for the cell.</param>
    /// <param name="manifest">The cell's workflow manifest.</param>
    /// <param name="recentExperience">Recent division outcomes for the same lineage (may be empty).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<DivisionPlan?> EvaluateAsync(
        ComplexitySnapshot snapshot,
        WorkflowManifest manifest,
        IReadOnlyList<DivisionExperience> recentExperience,
        CancellationToken ct = default) =>
        EvaluateAsync(snapshot, manifest, ct);
}
