namespace Ananke.Orchestration.Agents;

/// <summary>
/// Associates an <see cref="IAgentModel"/> with metadata about its capabilities, cost,
/// context window, and latency. Used by <see cref="CapabilityModelRouter"/> to select
/// the most cost-effective model that satisfies a given <see cref="TaskRequirements"/>.
/// </summary>
/// <example>
/// <code>
/// var profile = new ModelProfile
/// {
///     Name = "gpt-4o-mini",
///     Model = OpenAIChatAgentModel.Create(apiKey, "gpt-4o-mini"),
///     Capabilities = ModelCapability.TextGeneration | ModelCapability.ToolCalling | ModelCapability.StructuredOutput,
///     IntelligenceTier = 2,
///     CostPer1KTokens = 0.15m,
///     MaxContextTokens = 128_000,
///     SpeedTier = 4
/// };
/// </code>
/// </example>
public sealed record ModelProfile
{
    /// <summary>Human-readable model name (e.g. "gpt-4o-mini").</summary>
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
    /// Relative cost per 1 K tokens. The unit is chosen by the consumer
    /// (dollars, credits, or arbitrary weight) — only the relative ordering matters.
    /// </summary>
    public decimal CostPer1KTokens { get; init; }

    /// <summary>Maximum context window in tokens.</summary>
    public int MaxContextTokens { get; init; }

    /// <summary>
    /// Speed tier from 1 (slow) to 5 (fast). Used for latency-optimised routing.
    /// </summary>
    public int SpeedTier { get; init; } = 1;

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
