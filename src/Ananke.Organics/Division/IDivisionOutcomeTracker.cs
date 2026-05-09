namespace Ananke.Organics.Division;

/// <summary>
/// Tracks division outcomes and reinforces or contradicts the empirical entries
/// that influenced the division decision. This closes the learning loop:
/// division strategies that improve metrics are reinforced; those that worsen
/// metrics are contradicted and eventually decayed by <c>IOfflineLearner</c>.
/// </summary>
public interface IDivisionOutcomeTracker
{
    /// <summary>
    /// Record the baseline metrics of a cell that is about to divide.
    /// Called before <see cref="IWorkflowDivider.DivideAsync"/>.
    /// </summary>
    /// <param name="divisionId">Unique identifier for this division event.</param>
    /// <param name="parentBaseline">Complexity snapshot of the parent before division.</param>
    void RecordBaseline(string divisionId, ComplexitySnapshot parentBaseline);

    /// <summary>
    /// After enough post-division executions, compute the reward by comparing
    /// child metrics to the parent baseline. Reinforces or contradicts the
    /// empirical entries that influenced the original division decision.
    /// </summary>
    /// <param name="divisionId">The division event to evaluate.</param>
    /// <param name="childSnapshots">Current complexity snapshots of the child cells.</param>
    /// <param name="originalPlan">The plan that was executed, including <see cref="DivisionPlan.InfluencingEntries"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RewardAsync(
        string divisionId,
        IReadOnlyList<ComplexitySnapshot> childSnapshots,
        DivisionPlan originalPlan,
        CancellationToken ct = default);
}
