using Ananke.Abstractions.Tools;
using Ananke.Abstractions.Tools.Routing;

namespace Ananke.Orchestration.Tools.Routing;

/// <summary>
/// Routing stage that selects candidates via <see cref="IToolMemory.RecallAsync"/>
/// and intersects the result with the current candidate set.
/// </summary>
/// <remarks>
/// <para>
/// Cold-start fallback: when <see cref="IToolMemory.RecallAsync"/> returns an empty
/// list (no entries registered yet) the stage returns all current candidates with
/// <see cref="RoutingConfidence.Low"/> so the composite escalates rather than
/// silently dropping tools.
/// </para>
/// <para>
/// Intersection is keyed on <c>(KitName, ToolName)</c> to honour the subset
/// invariant — the stage never introduces tools absent from the input candidates.
/// </para>
/// </remarks>
public sealed class SemanticRecallStage : ISmartToolRouter
{
    private readonly IToolMemory _memory;
    private readonly int _topK;

    /// <summary>Creates the stage.</summary>
    /// <param name="memory">Tool memory to recall from.</param>
    /// <param name="topK">Maximum entries to retrieve from memory per turn. Defaults to 8.</param>
    public SemanticRecallStage(IToolMemory memory, int topK = 8)
    {
        ArgumentNullException.ThrowIfNull(memory);
        _memory = memory;
        _topK = topK;
    }

    /// <inheritdoc />
    public async Task<ToolRoutingDecision> RouteAsync(
        ToolRoutingRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var recalled = await _memory.RecallAsync(request.UserMessage, _topK, ct: ct).ConfigureAwait(false);

        // Cold-start fallback
        if (recalled.Count == 0)
        {
            return new ToolRoutingDecision
            {
                UseTools = true,
                SelectedTools = request.Candidates,
                Confidence = RoutingConfidence.Low,
                Rationale = "semantic recall empty — escalating",
            };
        }

        // Intersect recalled entries with current candidates by (KitName, ToolName)
        var recalledKeys = recalled
            .Select(e => (e.KitName, e.ToolName))
            .ToHashSet();

        var selected = request.Candidates
            .Where(e => recalledKeys.Contains((e.KitName, e.ToolName)))
            .ToList();

        return new ToolRoutingDecision
        {
            UseTools = true,
            SelectedTools = selected,
            Confidence = RoutingConfidence.High,
        };
    }
}
