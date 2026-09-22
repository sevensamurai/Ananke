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

    /// <summary>
    /// Maximum context window in tokens, as a property of the model itself.
    /// </summary>
    /// <remarks>
    /// For a self-hosted model this describes the weights, not the deployment, and the two routinely
    /// disagree — see <see cref="LocalDeployment.EffectiveContextTokens"/>. Read
    /// <see cref="ContextTokens"/> rather than this field when deciding whether a prompt fits.
    /// </remarks>
    public int MaxContextTokens { get; init; }

    /// <summary>
    /// The context window that actually binds: the serving runtime's configured window when
    /// <see cref="Deployment"/> records one, otherwise <see cref="MaxContextTokens"/>.
    /// </summary>
    /// <remarks>
    /// <b>Anything asking "will this prompt fit?" should read this, not
    /// <see cref="MaxContextTokens"/>.</b> <see cref="Satisfies"/> does. So should a custom scorer
    /// passed to <see cref="CapabilityModelRouter"/> — a scorer written against
    /// <see cref="MaxContextTokens"/> will happily rank a locally served model on a context window
    /// it does not have.
    /// </remarks>
    public int ContextTokens => Deployment?.EffectiveContextTokens ?? MaxContextTokens;

    /// <summary>
    /// How this model is being served, when it is self-hosted. <see langword="null"/> for a hosted
    /// model — and its presence is what distinguishes the two, which is why there is no separate
    /// "is local" flag to keep in step with it.
    /// </summary>
    public LocalDeployment? Deployment { get; init; }

    /// <summary>
    /// Speed tier from 1 (slow) to 5 (fast). Used for latency-optimised routing.
    /// </summary>
    public int SpeedTier { get; init; } = 1;

    /// <summary>
    /// Lifecycle stage of <see cref="Name"/>. Defaults to <see cref="ModelStatus.Current"/> for
    /// profiles constructed directly — only catalog-sourced profiles
    /// (<see cref="ModelProfileTemplate.ToProfile(IAgentModel, ModelCostRates)"/>) carry a
    /// curated value. <see cref="CapabilityModelRouter"/> logs a once-per-process warning when
    /// it routes to a <see cref="ModelStatus.Deprecated"/> profile.
    /// </summary>
    public ModelStatus Status { get; init; } = ModelStatus.Current;

    /// <summary>Recommended replacement model name when <see cref="Status"/> is not <see cref="ModelStatus.Current"/>.</summary>
    public string? ReplacedBy { get; init; }

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
    /// <remarks>
    /// <para>
    /// A profile declaring <see cref="ModelCapability.None"/> never satisfies anything, even a
    /// requirement set that asks for nothing. That is the difference between "this model does
    /// nothing" and "nobody has said what this model does": routing must not pick a model on the
    /// strength of an absence. Call the model directly if you know what it can do.
    /// </para>
    /// <para>
    /// Context is checked against <see cref="ContextTokens"/>, so a self-hosted model is measured by
    /// the window its server was launched with rather than by the one its weights support.
    /// </para>
    /// </remarks>
    public bool Satisfies(TaskRequirements requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        if (Capabilities == ModelCapability.None)
            return false;

        return (Capabilities & requirements.RequiredCapabilities) == requirements.RequiredCapabilities
            && IntelligenceTier >= requirements.MinIntelligenceTier
            && (requirements.MinContextTokens <= 0 || ContextTokens >= requirements.MinContextTokens);
    }

    /// <summary>
    /// Creates a profile for a model Ananke has no catalogue entry for, with the developer
    /// declaring what it can do as a coarse <see cref="ModelTier"/>.
    /// </summary>
    /// <remarks>
    /// For bring-your-own model ids — an Amazon Bedrock model, a Microsoft Foundry deployment name,
    /// a local endpoint. The tier expands to ordinary <see cref="ModelCapability"/> flags, so
    /// <see cref="CapabilityModelRouter"/> needs no new concept to route on.
    /// </remarks>
    /// <param name="name">Display name used in routing diagnostics.</param>
    /// <param name="model">The model itself.</param>
    /// <param name="tier">What the developer is asserting the model can do.</param>
    /// <param name="maxContextTokens">Context window, when known. <c>0</c> means unknown.</param>
    public static ModelProfile ForTier(
        string name, IAgentModel model, ModelTier tier, int maxContextTokens = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(model);

        return new ModelProfile
        {
            Name = name,
            Model = model,
            DeclaredTier = tier,
            Capabilities = tier.ToCapabilities(),
            MaxContextTokens = maxContextTokens,
        };
    }

    /// <summary>
    /// Creates a profile for a self-hosted model, with the developer declaring what it can do and
    /// how it is being served.
    /// </summary>
    /// <remarks>
    /// The same declaration rule as the other <c>ForTier</c> overload — the tier is an assertion by
    /// the developer, never an inference — with the deployment carrying the facts that belong to the
    /// server rather than to the weights. When
    /// <see cref="LocalDeployment.EffectiveContextTokens"/> is set it overrides
    /// <paramref name="maxContextTokens"/> for every routing decision.
    /// </remarks>
    /// <param name="name">Display name used in routing diagnostics.</param>
    /// <param name="model">The model itself.</param>
    /// <param name="tier">What the developer is asserting the model can do.</param>
    /// <param name="deployment">How the model is being served.</param>
    /// <param name="maxContextTokens">
    /// The weights' context window, when known. <c>0</c> means unknown, and is the right value when
    /// only the deployment's window is known.
    /// </param>
    public static ModelProfile ForTier(
        string name, IAgentModel model, ModelTier tier, LocalDeployment deployment, int maxContextTokens = 0)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        return ForTier(name, model, tier, maxContextTokens) with { Deployment = deployment };
    }

    /// <summary>
    /// Creates a profile for a model whose capabilities nobody has declared. It can be called
    /// directly but will never be selected by <see cref="CapabilityModelRouter"/>.
    /// </summary>
    /// <param name="name">Display name used in routing diagnostics.</param>
    /// <param name="model">The model itself.</param>
    public static ModelProfile Undeclared(string name, IAgentModel model) =>
        ForTier(name, model, ModelTier.Undeclared);

    /// <summary>
    /// The tier a developer declared for a bring-your-own model, or
    /// <see cref="ModelTier.Undeclared"/> for catalogue models, which state their capabilities
    /// directly instead.
    /// </summary>
    public ModelTier DeclaredTier { get; init; } = ModelTier.Undeclared;

    /// <summary>
    /// Descriptive facts about the model — size, whether it can be self-hosted, licence, family.
    /// Never consulted by <see cref="Satisfies"/>; used for filtering and for
    /// <see cref="CapabilityModelRouter.WithPolicy"/>.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="ModelClassification.Unspecified"/>. Catalogue-sourced profiles carry
    /// the template's value; a hand-built profile carries whatever the caller sets.
    /// </remarks>
    public ModelClassification Classification { get; init; } = ModelClassification.Unspecified;
}
