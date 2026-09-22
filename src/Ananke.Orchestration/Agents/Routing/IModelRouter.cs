using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents.Routing;

public interface IModelRouter
{
    IAgentModel Select(AgentRequest request);
}

/// <summary>
/// Optional extension of <see cref="IModelRouter"/> that exposes per-request cost rates
/// based on the model that would be selected. Implement this on routers backed by
/// <see cref="ModelProfile"/> metadata to enable accurate per-call cost tracking in
/// multi-model workflows.
/// </summary>
public interface IModelCostResolver
{
    /// <summary>
    /// Returns the <see cref="ModelCostRates"/> for the model that would handle
    /// <paramref name="request"/>. Returns <see cref="ModelCostRates.Zero"/> when
    /// cost information is not available (e.g. local models).
    /// </summary>
    ModelCostRates ResolveCostRates(AgentRequest request);
}

/// <summary>
/// Optional extension of <see cref="IModelRouter"/> that exposes the <em>real</em> context window
/// of the model that would handle a request.
/// </summary>
/// <remarks>
/// The counterpart of <see cref="IModelCostResolver"/>, and it exists for the same reason: the
/// window belongs to the model that is actually selected, which is not known until selection has
/// happened. Without it <see cref="ModelProfile.ContextTokens"/> is read when *choosing* a model and
/// never again by anything that fills that model's window.
/// </remarks>
public interface IModelContextResolver
{
    /// <summary>
    /// Returns the context window of the model that would handle <paramref name="request"/>.
    /// Returns <see cref="ModelContextWindow.Unknown"/> when it cannot be determined.
    /// </summary>
    ModelContextWindow ResolveContextWindow(AgentRequest request);
}
