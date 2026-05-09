using Ananke.Abstractions.Tools;
using Ananke.Abstractions.Tools.Routing;

namespace Ananke.Orchestration.Tools.Routing;

/// <summary>
/// Routing stage that drops candidates whose <see cref="ToolHealth"/> is
/// <see cref="ToolHealth.Offline"/> or <see cref="ToolHealth.Cooldown"/>
/// using the health recorded on each <see cref="ToolMemoryEntry"/>.
/// </summary>
/// <remarks>
/// Health state is read directly from <see cref="ToolMemoryEntry.Health"/>.
/// Place this stage early in the chain so that downstream stages only see
/// tools that are currently usable.
/// </remarks>
public sealed class HealthFilterStage : ISmartToolRouter
{
    /// <inheritdoc />
    public Task<ToolRoutingDecision> RouteAsync(
        ToolRoutingRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var selected = request.Candidates
            .Where(e => e.Health is not ToolHealth.Offline and not ToolHealth.Cooldown)
            .ToList();

        return Task.FromResult(new ToolRoutingDecision
        {
            UseTools = true,
            SelectedTools = selected,
            Confidence = RoutingConfidence.High,
        });
    }
}
