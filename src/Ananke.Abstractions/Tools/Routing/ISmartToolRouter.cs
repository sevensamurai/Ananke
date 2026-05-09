namespace Ananke.Abstractions.Tools.Routing;

/// <summary>
/// Strategy that narrows / re-ranks the tool surface for one model turn.
/// Implementations may run a heuristic, an embedding lookup,
/// a small LLM, or compose other routers in a chain.
/// </summary>
/// <remarks>
/// A router is an opt-in pre-flight pass executed by the orchestration
/// middleware before the frontier <c>IAgentModel</c> sees the request.
/// When no router is configured the kit's tools are passed through
/// unchanged — backward compatible.
/// </remarks>
public interface ISmartToolRouter
{
    /// <summary>Decide which tools the next model call should see.</summary>
    Task<ToolRoutingDecision> RouteAsync(
        ToolRoutingRequest request,
        CancellationToken ct = default);
}
