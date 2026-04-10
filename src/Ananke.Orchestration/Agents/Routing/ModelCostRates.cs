using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents.Routing;

/// <summary>
/// Per-model input and output token cost rates used by the budget system to compute
/// accurate costs in multi-model workflows. For local models (e.g. Ollama, llama.cpp),
/// use <see cref="Zero"/>.
/// </summary>
/// <param name="CostPer1KInputTokens">Cost per 1,000 input (prompt) tokens.</param>
/// <param name="CostPer1KOutputTokens">Cost per 1,000 output (completion) tokens.</param>
public sealed record ModelCostRates(decimal CostPer1KInputTokens, decimal CostPer1KOutputTokens)
{
    /// <summary>Zero-cost rates for local or self-hosted models (Ollama, llama.cpp, vLLM, etc.).</summary>
    public static ModelCostRates Zero { get; } = new(0m, 0m);

    /// <summary>
    /// Creates a <see cref="ModelCostRates"/> with the same rate for both input and output tokens.
    /// Useful when a provider charges a single blended rate.
    /// </summary>
    public static ModelCostRates Uniform(decimal costPer1KTokens) => new(costPer1KTokens, costPer1KTokens);

    /// <summary>
    /// Calculates the estimated cost for the given <paramref name="usage"/>.
    /// Returns <c>0</c> for zero-cost models.
    /// </summary>
    public decimal EstimateCost(TokenUsage usage) =>
        (usage.InputTokens / 1000m * CostPer1KInputTokens) +
        (usage.OutputTokens / 1000m * CostPer1KOutputTokens);
}
