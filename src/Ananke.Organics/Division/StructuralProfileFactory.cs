using Ananke.Orchestration.Tools;
using Ananke.Organics.Kernel.Snapshots;

namespace Ananke.Organics.Division;

/// <summary>
/// Derives a <see cref="StructuralProfile"/> from a <see cref="ToolKit"/>
/// or <see cref="WorkflowSnapshot"/>, removing the need for manual profile construction.
/// </summary>
public static class StructuralProfileFactory
{
    /// <summary>
    /// Derives structural metrics from a tool kit and job count.
    /// </summary>
    /// <param name="toolKit">The tool kit to analyze.</param>
    /// <param name="jobCount">Number of jobs in the workflow topology.</param>
    /// <param name="tagClusterOverride">
    /// Explicit tag cluster count. When <see langword="null"/>, estimated from
    /// tool name prefixes (distinct first words, clamped to [1, ToolCount/2]).
    /// </param>
    /// <param name="resourceSpanOverride">
    /// Explicit resource span. Defaults to 1 when <see langword="null"/>.
    /// </param>
    /// <param name="contextUtilizationOverride">
    /// Explicit context utilization (0.0–1.0). When <see langword="null"/>,
    /// estimated as <c>ToolCount × 0.05</c> (each tool ≈ 5% of context).
    /// </param>
    public static StructuralProfile FromToolKit(
        ToolKit toolKit,
        int jobCount,
        int? tagClusterOverride = null,
        int? resourceSpanOverride = null,
        float? contextUtilizationOverride = null)
    {
        ArgumentNullException.ThrowIfNull(toolKit);
        ArgumentOutOfRangeException.ThrowIfLessThan(jobCount, 1);

        var toolCount = toolKit.Tools.Count;

        var tagClusters = tagClusterOverride
            ?? EstimateTagClusters(toolKit);

        var contextUtil = contextUtilizationOverride
            ?? Math.Clamp(toolCount * 0.05f, 0f, 1f);

        return new StructuralProfile
        {
            ToolCount = toolCount,
            JobCount = jobCount,
            TagClusterCount = tagClusters,
            ResourceSpan = resourceSpanOverride ?? 1,
            ContextUtilization = contextUtil
        };
    }

    /// <summary>
    /// Derives structural metrics from a <see cref="WorkflowSnapshot"/>.
    /// </summary>
    public static StructuralProfile FromSnapshot(WorkflowSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var toolCount = snapshot.Tools.Count;
        var jobCount = Math.Max(1, snapshot.Jobs.Count);

        return new StructuralProfile
        {
            ToolCount = toolCount,
            JobCount = jobCount,
            TagClusterCount = Math.Max(1, EstimateTagClustersFromNames(snapshot.Tools)),
            ResourceSpan = 1,
            ContextUtilization = Math.Clamp(toolCount * 0.05f, 0f, 1f)
        };
    }

    /// <summary>
    /// Estimates tag clusters from tool name prefixes. Distinct first words
    /// (before <c>_</c> or <c>-</c>) are treated as domain indicators.
    /// Result is clamped to [1, ToolCount/2].
    /// </summary>
    private static int EstimateTagClusters(ToolKit toolKit)
    {
        var toolCount = toolKit.Tools.Count;
        if (toolCount == 0)
            return 1;

        var distinctPrefixes = EstimateTagClustersFromNames(toolKit.Tools.Keys);
        return Math.Clamp(distinctPrefixes, 1, Math.Max(1, toolCount / 2));
    }

    private static int EstimateTagClustersFromNames(IEnumerable<string> names)
    {
        return names
            .Select(GetPrefix)
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    private static string GetPrefix(string name)
    {
        var sep = name.AsSpan().IndexOfAny('_', '-');
        return sep > 0 ? name[..sep] : name;
    }
}
