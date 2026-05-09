using System.Collections.Concurrent;
using Ananke.Learning;

using Ananke.Learning.EmpiricalMemory;

namespace Ananke.Organics.Division;

/// <summary>
/// Tracks division outcomes and closes the learning loop by reinforcing or
/// contradicting the empirical entries that influenced the division decision.
/// </summary>
/// <remarks>
/// <para>
/// <b>Workflow:</b>
/// </para>
/// <list type="number">
///   <item>Before division: call <see cref="RecordBaseline"/> with the parent's
///     <see cref="ComplexitySnapshot"/>.</item>
///   <item>After enough post-division executions: call <see cref="RewardAsync"/>
///     with the child snapshots and the original <see cref="DivisionPlan"/>.</item>
///   <item>The tracker compares child metrics to the parent baseline, computes a
///     reward signal, and reinforces or contradicts the
///     <see cref="DivisionPlan.InfluencingEntries"/>.</item>
/// </list>
/// <para>
/// Reward computation weights:
/// </para>
/// <list type="bullet">
///   <item>Routing entropy improvement: 40% — lower entropy = more focused specialist</item>
///   <item>Context utilization improvement: 30% — less tool bloat in context window</item>
///   <item>Latency improvement: 30% — faster response times</item>
/// </list>
/// </remarks>
/// <param name="memory">Shared empirical memory used to reinforce/contradict entries.</param>
public sealed class DivisionOutcomeTracker(IEmpiricalMemory memory) : IDivisionOutcomeTracker
{
    private readonly ConcurrentDictionary<string, ComplexitySnapshot> _baselines = new();

    /// <inheritdoc />
    public void RecordBaseline(string divisionId, ComplexitySnapshot parentBaseline)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(divisionId);
        ArgumentNullException.ThrowIfNull(parentBaseline);
        _baselines[divisionId] = parentBaseline;
    }

    /// <inheritdoc />
    public async Task RewardAsync(
        string divisionId,
        IReadOnlyList<ComplexitySnapshot> childSnapshots,
        DivisionPlan originalPlan,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(divisionId);
        ArgumentNullException.ThrowIfNull(childSnapshots);
        ArgumentNullException.ThrowIfNull(originalPlan);

        if (!_baselines.TryGetValue(divisionId, out var baseline))
            throw new InvalidOperationException(
                $"No baseline recorded for division '{divisionId}'. Call RecordBaseline first.");

        if (originalPlan.InfluencingEntries.Count == 0)
            return;

        var reward = ComputeReward(baseline, childSnapshots);

        foreach (var entryId in originalPlan.InfluencingEntries)
        {
            ct.ThrowIfCancellationRequested();

            if (reward < -0.3f)
            {
                // Strongly negative outcome — contradict the strategy
                await memory.ContradictAsync(entryId,
                    $"Division '{divisionId}' worsened metrics (reward: {reward:F2})", ct);
            }
            else
            {
                // Reinforce with the reward signal (positive or mildly negative)
                await memory.ReinforceAsync(entryId, new Reinforcement
                {
                    NewEvidence = [$"division-outcome:{divisionId}:reward={reward:F2}"],
                    Source = "division-outcome-tracker",
                    Reward = NormalizeReward(reward)
                }, ct);
            }
        }
    }

    /// <summary>
    /// Computes a reward in [-1.0, +1.0] by comparing child metrics to the
    /// parent baseline. Positive = children improved, negative = children worsened.
    /// </summary>
    public static float ComputeReward(
        ComplexitySnapshot parent,
        IReadOnlyList<ComplexitySnapshot> children)
    {
        if (children.Count == 0)
            return 0f;

        var avgChildEntropy = children.Average(c => c.RoutingEntropy);
        var avgChildContextUtil = children.Average(c => c.ContextUtilization);
        var avgChildLatency = children.Average(c => c.AvgLatencyMs);

        // Improvement = parent value - child average (positive = children better)
        var entropyImprovement = parent.RoutingEntropy - avgChildEntropy;
        var contextImprovement = parent.ContextUtilization - avgChildContextUtil;

        var latencyImprovement = parent.AvgLatencyMs > 0
            ? (parent.AvgLatencyMs - avgChildLatency) / parent.AvgLatencyMs
            : 0f;

        // Weighted sum, clamped to [-1, 1]
        var raw = (entropyImprovement * 0.4f) +
                  (contextImprovement * 0.3f) +
                  (latencyImprovement * 0.3f);

        return Math.Clamp(raw, -1f, 1f);
    }

    /// <summary>
    /// Normalizes the raw reward from [-1, 1] to [0, 1] for
    /// <see cref="Reinforcement.Reward"/> (which represents outcome quality).
    /// </summary>
    private static float NormalizeReward(float raw) =>
        (raw + 1f) / 2f;
}
