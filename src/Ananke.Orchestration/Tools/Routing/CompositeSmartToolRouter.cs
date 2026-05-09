using Ananke.Abstractions.Tools.Routing;

namespace Ananke.Orchestration.Tools.Routing;

/// <summary>
/// Chains multiple <see cref="ISmartToolRouter"/> stages in sequence,
/// threading candidates from one stage into the next.
/// </summary>
/// <remarks>
/// <para>
/// Each stage receives as <see cref="ToolRoutingRequest.Candidates"/> the
/// <see cref="ToolRoutingDecision.SelectedTools"/> from the previous stage
/// (subject to the escalation and short-circuit rules below).
/// </para>
/// <para>
/// Invariants enforced per stage:
/// <list type="bullet">
///   <item>A stage may only return a <em>subset</em> of its input candidates
///   (compared by <c>(KitName, ToolName)</c>). Violations throw
///   <see cref="InvalidRoutingDecisionException"/>.</item>
///   <item>If <see cref="ToolRoutingDecision.Terminal"/> is <see langword="true"/>,
///   the chain stops immediately.</item>
///   <item>If <see cref="ToolRoutingDecision.UseTools"/> is <see langword="false"/> and
///   <see cref="ToolRoutingDecision.Confidence"/> is <see cref="RoutingConfidence.High"/>,
///   the chain short-circuits and returns "no tools".</item>
///   <item>If <see cref="ToolRoutingDecision.Confidence"/> is <see cref="RoutingConfidence.Low"/>,
///   the candidates are <em>not</em> narrowed for the next stage (escalation rule §4.3).</item>
/// </list>
/// </para>
/// <para>
/// When the stages list is empty the composite behaves like
/// <see cref="PassThroughRouter"/>.
/// </para>
/// </remarks>
public sealed class CompositeSmartToolRouter : ISmartToolRouter
{
    private readonly IReadOnlyList<ISmartToolRouter> _stages;

    /// <summary>Creates a composite from an ordered list of stages.</summary>
    /// <param name="stages">Ordered routing stages. May be empty.</param>
    public CompositeSmartToolRouter(IReadOnlyList<ISmartToolRouter> stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        _stages = stages;
    }

    /// <inheritdoc />
    public async Task<ToolRoutingDecision> RouteAsync(
        ToolRoutingRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_stages.Count == 0)
            return await PassThroughRouter.Instance.RouteAsync(request, ct).ConfigureAwait(false);

        var current = request;
        ToolRoutingDecision decision = null!;

        foreach (var stage in _stages)
        {
            decision = await stage.RouteAsync(current, ct).ConfigureAwait(false);

            // Subset invariant check
            var allowedKeys = current.Candidates
                .Select(e => (e.KitName, e.ToolName))
                .ToHashSet();

            foreach (var selected in decision.SelectedTools)
            {
                if (!allowedKeys.Contains((selected.KitName, selected.ToolName)))
                    throw new InvalidRoutingDecisionException(
                        $"Stage '{stage.GetType().Name}' returned tool " +
                        $"'{selected.KitName}/{selected.ToolName}' which was not present in the candidates. " +
                        "Stages must only return a subset of their input candidates.");
            }

            // Terminal — stop the chain
            if (decision.Terminal)
                break;

            // High-confidence "no tools" short-circuit
            if (!decision.UseTools && decision.Confidence == RoutingConfidence.High)
                break;

            // Low confidence — escalation: do not narrow, carry original candidates forward
            if (decision.Confidence == RoutingConfidence.Low)
            {
                current = current with { Candidates = current.Candidates };
                continue;
            }

            // Narrow candidates for the next stage
            current = current with { Candidates = decision.SelectedTools };
        }

        // Clamp to MaxSelected — pinned tools are preserved first, then fill remaining slots.
        if (decision.UseTools && decision.SelectedTools.Count > request.MaxSelected)
        {
            var pinned = request.PinnedTools.Count > 0
                ? decision.SelectedTools
                    .Where(t => request.PinnedTools.Any(p => p.KitName == t.KitName && p.ToolName == t.ToolName))
                    .ToList()
                : [];

            var remaining = decision.SelectedTools
                .Where(t => !pinned.Any(p => p.KitName == t.KitName && p.ToolName == t.ToolName))
                .Take(Math.Max(0, request.MaxSelected - pinned.Count))
                .ToList();

            decision = decision with
            {
                SelectedTools = [.. pinned, .. remaining],
            };
        }

        return decision;
    }
}
