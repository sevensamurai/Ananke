using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents.Routing;

/// <summary>
/// Associates an <see cref="IAgentModel"/> with metadata about its capabilities, cost,
/// context window, and latency. Used by <see cref="CapabilityModelRouter"/> to select
/// the most cost-effective model that satisfies a given <see cref="TaskRequirements"/>.
/// </summary>
/// <example>
/// <code>
/// // Cloud model with split rates
/// var cloud = new ModelProfile
/// {
///     Name = "gpt-4.1-mini",
///     Model = OpenAIChatAgentModel.Create(apiKey, "gpt-4.1-mini"),
///     Capabilities = ModelCapability.TextGeneration | ModelCapability.ToolCalling | ModelCapability.StructuredOutput,
///     IntelligenceTier = 2,
///     CostPer1KInputTokens = 0.0004m,
///     CostPer1KOutputTokens = 0.0016m,
///     MaxContextTokens = 1_047_576,
///     SpeedTier = 4
/// };
///
/// // Local model (zero cost)
/// var local = new ModelProfile
/// {
///     Name = "llama3.2:3b",
///     Model = ollamaModel,
///     Capabilities = ModelCapability.TextGeneration,
///     IntelligenceTier = 1,
///     MaxContextTokens = 128_000,
///     SpeedTier = 5
/// };
/// </code>
/// </example>
public sealed record ModelProfile
{
    /// <summary>Human-readable model name (e.g. "gpt-4.1-mini", "llama3.2:3b").</summary>
    public required string Name { get; init; }

    /// <summary>The underlying model instance.</summary>
    public required IAgentModel Model { get; init; }

    /// <summary>Capability flags this model supports.</summary>
    public ModelCapability Capabilities { get; init; } = ModelCapability.TextGeneration;

    /// <summary>
    /// Intelligence tier from 1 (basic) to 5 (frontier). Higher tiers indicate
    /// stronger reasoning, instruction-following, and output quality.
    /// </summary>
    public int IntelligenceTier { get; init; } = 1;

    /// <summary>
    /// Blended cost per 1 K tokens used for relative routing comparisons.
    /// When <see cref="CostPer1KInputTokens"/> and <see cref="CostPer1KOutputTokens"/>
    /// are set, this value is still used for routing sort order — set it to a
    /// representative blended rate. Defaults to <c>0</c> (free / local model).
    /// </summary>
    public decimal CostPer1KTokens { get; init; }

    /// <summary>
    /// Cost per 1,000 input (prompt) tokens for accurate budget tracking.
    /// For local / self-hosted models (Ollama, llama.cpp, vLLM), leave at the
    /// default of <c>0</c>.
    /// </summary>
    public decimal CostPer1KInputTokens { get; init; }

    /// <summary>
    /// Cost per 1,000 output (completion) tokens for accurate budget tracking.
    /// For local / self-hosted models (Ollama, llama.cpp, vLLM), leave at the
    /// default of <c>0</c>.
    /// </summary>
    public decimal CostPer1KOutputTokens { get; init; }

    /// <summary>Maximum context window in tokens.</summary>
    public int MaxContextTokens { get; init; }

    /// <summary>
    /// Speed tier from 1 (slow) to 5 (fast). Used for latency-optimised routing.
    /// </summary>
    public int SpeedTier { get; init; } = 1;

    /// <summary>
    /// Resolves the cost rates for this model. Uses <see cref="CostPer1KInputTokens"/> and
    /// <see cref="CostPer1KOutputTokens"/> when set, otherwise falls back to
    /// <see cref="CostPer1KTokens"/> for both. Returns <see cref="ModelCostRates.Zero"/>
    /// for local models where all cost properties are <c>0</c>.
    /// </summary>
    public ModelCostRates GetCostRates()
    {
        if (CostPer1KInputTokens != 0 || CostPer1KOutputTokens != 0)
            return new ModelCostRates(CostPer1KInputTokens, CostPer1KOutputTokens);

        if (CostPer1KTokens != 0)
            return ModelCostRates.Uniform(CostPer1KTokens);

        return ModelCostRates.Zero;
    }

    /// <summary>
    /// Returns <c>true</c> when this profile meets every constraint in
    /// <paramref name="requirements"/> — capabilities, intelligence, and context size.
    /// </summary>
    public bool Satisfies(TaskRequirements requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        return (Capabilities & requirements.RequiredCapabilities) == requirements.RequiredCapabilities
            && IntelligenceTier >= requirements.MinIntelligenceTier
            && (requirements.MinContextTokens <= 0 || MaxContextTokens >= requirements.MinContextTokens);
    }
}
