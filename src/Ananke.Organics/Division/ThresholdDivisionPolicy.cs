using Ananke.Design;

namespace Ananke.Organics.Division;

/// <summary>
/// Cold-start <see cref="IDivisionPolicy"/> that evaluates surface tension
/// using simple structural thresholds. Used when the kernel has no division
/// experience yet (no empirical memory entries tagged <c>"division"</c>).
/// </summary>
/// <remarks>
/// <para>
/// The two required conditions for division are:
/// </para>
/// <list type="bullet">
///   <item><see cref="ComplexitySnapshot.ToolCount"/> ≥ <c>minTools</c></item>
///   <item><see cref="ComplexitySnapshot.TagClusterCount"/> ≥ <c>minClusters</c></item>
/// </list>
/// <para>
/// When both are met, the policy uses an injected <paramref name="clusterStrategy"/>
/// to propose how tools should be split across children. When no strategy is
/// provided, it falls back to splitting manifest jobs evenly into two children.
/// </para>
/// </remarks>
/// <param name="minTools">Minimum tool count before division is considered. Default: 6.</param>
/// <param name="minClusters">Minimum tag clusters before division is considered. Default: 2.</param>
/// <param name="clusterStrategy">
/// Optional custom strategy to derive <see cref="ChildSpec"/> entries from a manifest.
/// When <see langword="null"/>, jobs are split evenly into two children.
/// </param>
public sealed class ThresholdDivisionPolicy(
    int minTools = 6,
    int minClusters = 2,
    Func<string, WorkflowManifest, IReadOnlyList<ChildSpec>>? clusterStrategy = null)
    : IDivisionPolicy
{
    /// <inheritdoc />
    public Task<DivisionPlan?> EvaluateAsync(
        ComplexitySnapshot snapshot,
        WorkflowManifest manifest,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(manifest);

        if (snapshot.ToolCount < minTools || snapshot.TagClusterCount < minClusters)
            return Task.FromResult<DivisionPlan?>(null);

        var children = clusterStrategy is not null
            ? clusterStrategy(snapshot.WorkflowName, manifest)
            : DefaultSplit(snapshot.WorkflowName, manifest);

        if (children.Count < 2)
            return Task.FromResult<DivisionPlan?>(null);

        var plan = new DivisionPlan
        {
            ParentWorkflow = snapshot.WorkflowName,
            Children = children,
            Reason = $"Surface tension: {snapshot.ToolCount} tools across " +
                     $"{snapshot.TagClusterCount} tag clusters"
        };

        return Task.FromResult<DivisionPlan?>(plan);
    }

    /// <summary>
    /// Default split: divides manifest jobs into two roughly equal groups
    /// and assigns each group to a child cell.
    /// </summary>
    private static IReadOnlyList<ChildSpec> DefaultSplit(string parentName, WorkflowManifest manifest)
    {
        var jobNames = manifest.Jobs.Keys.ToList();
        if (jobNames.Count < 2)
            return [];

        var mid = jobNames.Count / 2;
        var groupA = jobNames.Take(mid).ToList();
        var groupB = jobNames.Skip(mid).ToList();

        return
        [
            new ChildSpec
            {
                Name = $"{parentName}-a",
                Domain = $"{parentName}-a",
                Tools = [],
                Jobs = groupA
            },
            new ChildSpec
            {
                Name = $"{parentName}-b",
                Domain = $"{parentName}-b",
                Tools = [],
                Jobs = groupB
            }
        ];
    }
}
