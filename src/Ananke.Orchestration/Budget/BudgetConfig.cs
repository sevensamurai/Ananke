using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Workflows;

namespace Ananke.Orchestration.Budget;

/// <summary>
/// Cost budget configuration for a workflow. When attached via
/// <see cref="Workflow{TState}.WithBudget(decimal)"/> or
/// <see cref="Workflow{TState}.WithBudget(decimal, decimal, decimal)"/>,
/// the runner tracks cumulative token usage across all LLM calls and terminates
/// the workflow when the estimated cost exceeds <see cref="MaxCost"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Multi-model workflows:</b> When models are selected via
/// <see cref="Agents.Routing.CapabilityModelRouter"/>, cost is computed per-call from the
/// selected <see cref="Agents.Routing.ModelProfile"/>'s rates. The flat rates on this class
/// serve as a fallback for models without profile cost data.
/// </para>
/// <para>
/// <b>Local models:</b> Models with zero cost (Ollama, llama.cpp, vLLM) are tracked
/// correctly — their calls don't contribute to cost but their tokens are still counted.
/// </para>
/// </remarks>
public sealed record BudgetConfig
{
    /// <summary>Maximum allowed estimated cost before the workflow is terminated.</summary>
    public required decimal MaxCost { get; init; }

    /// <summary>
    /// What happens on reaching <see cref="MaxCost"/>. Defaults to
    /// <see cref="BudgetMode.Stop"/>, which is the behaviour that shipped before this
    /// property existed.
    /// </summary>
    public BudgetMode Mode { get; init; } = BudgetMode.Stop;

    /// <summary>
    /// Optional earlier threshold that emits a warning without stopping the run.
    /// <c>null</c> disables the warning tier.
    /// </summary>
    /// <remarks>
    /// An absolute figure rather than a percentage of <see cref="MaxCost"/>: this is a spike
    /// guard, and an absolute number is what a person reasons about when asking to be told
    /// before spend gets out of hand. Orthogonal to <see cref="Mode"/> — you want warning
    /// <em>and</em> stopping, not warning instead of stopping.
    /// </remarks>
    public decimal? WarnAtCost { get; init; }

    /// <summary>
    /// Optional ceiling on spend accumulated across the whole billing period — the "$200 a
    /// month" figure. <c>null</c> disables period enforcement.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="MaxCost"/>, which is scoped to one execution and dies with it, this
    /// accumulates across runs and survives restarts. It therefore <b>requires a durable
    /// recorder</b>: configuring it without one is rejected at <c>Build()</c>, because a period
    /// ceiling backed by in-memory state resets on every process start and silently guards
    /// nothing.
    /// </remarks>
    public decimal? PeriodCostLimit { get; init; }

    /// <summary>
    /// Optional earlier threshold on period spend that warns without stopping. <c>null</c>
    /// disables it. Separate from <see cref="WarnAtCost"/> because the two figures differ by
    /// orders of magnitude — a run may cost cents against a monthly ceiling of hundreds.
    /// </summary>
    public decimal? WarnAtPeriodCost { get; init; }

    /// <summary>
    /// Fallback cost per 1,000 input (prompt) tokens, used when model-specific rates
    /// are not available. Defaults to <c>0</c>.
    /// </summary>
    public decimal CostPer1KInputTokens { get; init; }

    /// <summary>
    /// Fallback cost per 1,000 output (completion) tokens, used when model-specific rates
    /// are not available. Defaults to <c>0</c>.
    /// </summary>
    public decimal CostPer1KOutputTokens { get; init; }

    /// <summary>
    /// <c>true</c> when fallback cost rates are configured on this instance.
    /// </summary>
    internal bool HasFallbackRates => CostPer1KInputTokens != 0 || CostPer1KOutputTokens != 0;

    /// <summary>
    /// A budget whose fallback rates are given the way providers publish them — per
    /// <b>million</b> tokens.
    /// </summary>
    /// <param name="maxCost">Spend ceiling, in the same currency as the rates.</param>
    /// <param name="inputPerMillion">Cost per 1,000,000 input tokens, e.g. <c>0.15m</c>.</param>
    /// <param name="outputPerMillion">Cost per 1,000,000 output tokens, e.g. <c>0.60m</c>.</param>
    /// <remarks>
    /// Ananke ships no rate table and never will: any figure it carried would be wrong for
    /// anyone with negotiated pricing, committed-use discounts, credits, or a different region —
    /// and a budget that is confidently wrong is worse than one that admits it does not know.
    /// Rates are yours to declare, in the unit your provider quotes.
    /// </remarks>
    public static BudgetConfig FromPerMillion(
        decimal maxCost, decimal inputPerMillion, decimal outputPerMillion) => new()
        {
            MaxCost = maxCost,
            CostPer1KInputTokens = inputPerMillion / 1000m,
            CostPer1KOutputTokens = outputPerMillion / 1000m
        };

    /// <summary>
    /// Calculates the estimated cost for the given <paramref name="usage"/> using
    /// the flat fallback rates on this config.
    /// </summary>
    public decimal EstimateCost(TokenUsage usage) =>
        (usage.InputTokens / 1000m * CostPer1KInputTokens) +
        (usage.OutputTokens / 1000m * CostPer1KOutputTokens);
}
