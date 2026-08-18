using Ananke.Design;
using Ananke.Learning;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Learning.Exploration;

namespace Ananke.Organics.Division;

/// <summary>
/// Warm-start <see cref="IDivisionPolicy"/> that recalls division strategies
/// from <see cref="IEmpiricalMemory"/> (tagged <c>"division"</c>) and uses
/// <see cref="IExplorationStrategy"/> (UCB) to balance exploitation of proven
/// strategies vs. exploration of novel ones. Falls back to a delegate policy
/// (typically <see cref="ThresholdDivisionPolicy"/>) on cold start (no
/// division memories).
/// </summary>
/// <remarks>
/// <para>
/// This is the kernel's "division DNA" — the learned knowledge about when and
/// how to divide. Early on, the fallback's simple heuristics drive decisions.
/// Over time, as <see cref="IDivisionOutcomeTracker"/> reinforces/contradicts
/// entries, the kernel gets better at dividing.
/// </para>
/// <para>
/// <b>Cold start:</b> No entries tagged <c>"division"</c> → delegates entirely
/// to the fallback policy.
/// </para>
/// <para>
/// <b>Warm start:</b> Recalled entries become <see cref="ActionCandidate"/>s.
/// A synthetic "do not divide" candidate (score 0, high uncertainty) is always
/// included. The exploration strategy selects between dividing and not dividing.
/// When division is selected, the cluster strategy generates the
/// <see cref="DivisionPlan"/> and the recalled entry IDs are recorded in
/// <see cref="DivisionPlan.InfluencingEntries"/> for reward propagation.
/// </para>
/// </remarks>
/// <param name="memory">Empirical memory containing division strategies.</param>
/// <param name="strategy">Exploration strategy for action selection (e.g. UCB, ε-greedy).</param>
/// <param name="fallback">Cold-start policy used when no division memories exist.</param>
/// <param name="clusterStrategy">
/// Optional cluster strategy to generate <see cref="ChildSpec"/>s from a manifest.
/// When <see langword="null"/>, the fallback policy's plan (if any) is used.
/// </param>
public sealed class ExperienceDrivenDivisionPolicy(
    IEmpiricalMemory memory,
    IExplorationStrategy strategy,
    IDivisionPolicy fallback,
    Func<string, WorkflowManifest, IReadOnlyList<ChildSpec>>? clusterStrategy = null)
    : IDivisionPolicy
{
    private const string DivisionTag = "division";
    private const int MaxRecall = 10;

    /// <inheritdoc />
    public async Task<DivisionPlan?> EvaluateAsync(
        ComplexitySnapshot snapshot,
        WorkflowManifest manifest,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(manifest);

        // Recall division strategies from empirical memory
        var situation = $"Division evaluation for cell '{snapshot.WorkflowName}' " +
                        $"with {snapshot.ToolCount} tools, {snapshot.TagClusterCount} tag clusters, " +
                        $"entropy {snapshot.RoutingEntropy:F2}, context util {snapshot.ContextUtilization:P0}";

        var recalled = await memory.RecallAsync(situation, new RecallOptions
        {
            TopK = MaxRecall,
            RequiredTags = [DivisionTag],
            MinConfidence = 0.1f
        }, ct).ConfigureAwait(false);

        // Cold start: no division memories → delegate to fallback
        if (recalled.Count == 0)
            return await fallback.EvaluateAsync(snapshot, manifest, ct).ConfigureAwait(false);

        // Build action candidates from recalled entries + a "do not divide" option
        var candidates = BuildCandidates(recalled);
        var totalSelections = recalled.Sum(m => m.Entry.ObservationCount);
        var selectedIndex = strategy.SelectAction(candidates, totalSelections);

        // Index 0 is always "do not divide"
        if (selectedIndex == 0)
            return null;

        // Division selected — generate the plan
        var children = clusterStrategy is not null
            ? clusterStrategy(snapshot.WorkflowName, manifest)
            : await GenerateChildrenFromFallback(snapshot, manifest, ct).ConfigureAwait(false);

        if (children.Count < 2)
            return null;

        // The influencing entries are the recalled division memories (excluding
        // the synthetic "do not divide" entry at index 0)
        var influencingEntries = recalled
            .Select(m => m.Entry.Id)
            .ToList();

        return new DivisionPlan
        {
            ParentWorkflow = snapshot.WorkflowName,
            Children = children,
            Reason = $"Experience-driven: {recalled.Count} division strategies recalled, " +
                     $"selected action {selectedIndex} via {strategy.GetType().Name}",
            InfluencingEntries = influencingEntries
        };
    }

    /// <summary>
    /// Builds action candidates for the exploration strategy.
    /// Index 0 = "do not divide" (neutral score, high uncertainty).
    /// Index 1..N = one per recalled division memory.
    /// </summary>
    private static IReadOnlyList<ActionCandidate> BuildCandidates(
        IReadOnlyList<EmpiricalMatch> recalled)
    {
        var candidates = new List<ActionCandidate>(recalled.Count + 1);

        // "Do not divide" — neutral score, high uncertainty (allows exploration)
        candidates.Add(new ActionCandidate
        {
            Score = 0f,
            Uncertainty = 1.0f,
            SelectionCount = 0
        });

        // One candidate per recalled division strategy
        foreach (var match in recalled)
        {
            var entry = match.Entry;
            candidates.Add(new ActionCandidate
            {
                Score = entry.Valence * entry.Strength,
                Uncertainty = entry.Variance,
                SelectionCount = entry.ObservationCount
            });
        }

        return candidates;
    }

    /// <summary>
    /// Falls back to the delegate policy to get a plan, then extracts the children.
    /// </summary>
    private async Task<IReadOnlyList<ChildSpec>> GenerateChildrenFromFallback(
        ComplexitySnapshot snapshot,
        WorkflowManifest manifest,
        CancellationToken ct)
    {
        var fallbackPlan = await fallback.EvaluateAsync(snapshot, manifest, ct).ConfigureAwait(false);
        return fallbackPlan?.Children ?? (IReadOnlyList<ChildSpec>)[];
    }
}
