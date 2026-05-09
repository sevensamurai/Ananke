using Ananke.Abstractions.Tools;
using Ananke.Abstractions.Tools.Routing;

namespace Ananke.Orchestration.Tools.Routing;

/// <summary>
/// Pass-through <see cref="ISmartToolRouter"/> that returns all candidates unchanged.
/// This is the default router used when no explicit router is configured —
/// preserving full backward compatibility.
/// </summary>
public sealed class PassThroughRouter : ISmartToolRouter
{
    /// <summary>The singleton default instance.</summary>
    public static readonly PassThroughRouter Instance = new();

    /// <inheritdoc />
    public Task<ToolRoutingDecision> RouteAsync(
        ToolRoutingRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(new ToolRoutingDecision
        {
            UseTools = true,
            SelectedTools = request.Candidates,
            Confidence = RoutingConfidence.High,
        });
    }
}
