namespace Ananke.Orchestration.Agents;

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
