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
    /// Rates quoted the way providers publish them — per <b>million</b> tokens.
    /// </summary>
    /// <param name="input">Cost per 1,000,000 input tokens, e.g. <c>0.15m</c>.</param>
    /// <param name="output">Cost per 1,000,000 output tokens, e.g. <c>0.60m</c>.</param>
    /// <remarks>
    /// Prefer this over the per-1K constructor. Pricing pages quote per-million, so copying a
    /// published figure straight into <see cref="CostPer1KInputTokens"/> makes a budget 1000x
    /// too loose — a mistake that is invisible until the bill arrives. Conversion is exact:
    /// these are decimals, not floats.
    /// <para>
    /// Output tokens usually cost several times input, which is why a single token ceiling is a
    /// poor proxy for spend and the two rates are declared separately.
    /// </para>
    /// </remarks>
    public static ModelCostRates PerMillion(decimal input, decimal output) =>
        new(input / 1000m, output / 1000m);

    /// <summary>
    /// Calculates the estimated cost for the given <paramref name="usage"/>.
    /// Returns <c>0</c> for zero-cost models.
    /// </summary>
    public decimal EstimateCost(TokenUsage usage) =>
        (usage.InputTokens / 1000m * CostPer1KInputTokens) +
        (usage.OutputTokens / 1000m * CostPer1KOutputTokens);
}
