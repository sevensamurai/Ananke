using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Agents.Routing;

/// <summary>
/// A model profile template without an <see cref="IAgentModel"/> instance or pricing.
/// Contains stable metadata (capabilities, context limits, intelligence/speed tiers)
/// that does not change after a model's release.
/// <para>
/// Bind to a live model via <see cref="ToProfile(IAgentModel, ModelCostRates)"/> to create
/// a complete <see cref="ModelProfile"/> for use with <see cref="CapabilityModelRouter"/>.
/// </para>
/// </summary>
/// <example>
/// <code>
/// // With explicit pricing
/// var profile = ModelCatalog.OpenAI.Gpt4_1Mini
///     .ToProfile(myModel, new ModelCostRates(0.0004m, 0.0016m));
///
/// // Zero-cost local model
/// var local = ModelCatalog.Meta.Llama3_2_3B
///     .ToProfile(ollamaModel, ModelCostRates.Zero);
/// </code>
/// </example>
public sealed record ModelProfileTemplate
{
    /// <summary>Canonical model name (e.g. "gpt-4.1-mini").</summary>
    public required string Name { get; init; }

    /// <summary>Capability flags this model supports.</summary>
    public ModelCapability Capabilities { get; init; }

    /// <summary>Intelligence tier (1–5).</summary>
    public int IntelligenceTier { get; init; } = 1;

    /// <summary>Maximum context window in tokens.</summary>
    public int MaxContextTokens { get; init; }

    /// <summary>Speed tier (1–5).</summary>
    public int SpeedTier { get; init; } = 1;

    /// <summary>
    /// Creates a complete <see cref="ModelProfile"/> by binding this template
    /// to a live <paramref name="model"/> instance with explicit cost <paramref name="rates"/>.
    /// Use <see cref="ModelCostRates.Zero"/> for local / self-hosted models.
    /// </summary>
    public ModelProfile ToProfile(IAgentModel model, ModelCostRates rates)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(rates);
        return new ModelProfile
        {
            Name = Name,
            Model = model,
            Capabilities = Capabilities,
            IntelligenceTier = IntelligenceTier,
            CostPer1KTokens = (rates.CostPer1KInputTokens + rates.CostPer1KOutputTokens) / 2m,
            CostPer1KInputTokens = rates.CostPer1KInputTokens,
            CostPer1KOutputTokens = rates.CostPer1KOutputTokens,
            MaxContextTokens = MaxContextTokens,
            SpeedTier = SpeedTier
        };
    }

    /// <summary>
    /// Creates a <see cref="ModelProfile"/> for a zero-cost local or self-hosted model
    /// (Ollama, llama.cpp, vLLM). Shorthand for <c>ToProfile(model, ModelCostRates.Zero)</c>.
    /// </summary>
    public ModelProfile ToProfile(IAgentModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return ToProfile(model, ModelCostRates.Zero);
    }
}

/// <summary>
/// Registry of well-known commercial and open-weight model metadata.
/// <para>
/// Templates contain <b>stable</b> model characteristics — capabilities, context limits,
/// intelligence tiers, and speed tiers — that do not change after a model's release.
/// <b>Pricing is intentionally excluded</b> because it changes frequently; supply your
/// current rates via <see cref="ModelProfileTemplate.ToProfile(IAgentModel, ModelCostRates)"/>.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var router = new CapabilityModelRouter(RoutingStrategy.CheapestFit)
///     .AddModel(ModelCatalog.OpenAI.Gpt4_1Mini
///         .ToProfile(miniModel, new ModelCostRates(0.0004m, 0.0016m)))
///     .AddModel(ModelCatalog.OpenAI.Gpt4_1
///         .ToProfile(fullModel, new ModelCostRates(0.002m, 0.008m)))
///     .AddModel(ModelCatalog.Meta.Llama3_2_3B
///         .ToProfile(ollamaModel));  // zero cost
/// </code>
/// </example>
public static class ModelCatalog
{
    /// <summary>
    /// Looks up a well-known model by canonical name (case-insensitive).
    /// Returns <see langword="null"/> if the model is not in the catalog.
    /// </summary>
    public static ModelProfileTemplate? TryGet(string modelName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        return All.FirstOrDefault(t =>
            string.Equals(t.Name, modelName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>All registered model templates.</summary>
    public static IReadOnlyList<ModelProfileTemplate> All { get; } =
    [
        // OpenAI
        OpenAI.Gpt4_1, OpenAI.Gpt4_1Mini, OpenAI.Gpt4_1Nano,
        OpenAI.O3, OpenAI.O3Mini, OpenAI.O4Mini,
        OpenAI.Gpt4o, OpenAI.Gpt4oMini,

        // Anthropic
        Anthropic.Claude4Sonnet, Anthropic.Claude4Opus,
        Anthropic.Claude3_7Sonnet, Anthropic.Claude3_5Haiku,
        Anthropic.ClaudeOpus4_8, Anthropic.ClaudeSonnet4_6, Anthropic.ClaudeHaiku4_5,

        // Google
        Google.Gemini3_1Pro, Google.Gemini3_1Flash,
        Google.Gemini2_5Pro, Google.Gemini2_5Flash, Google.Gemini2_0Flash,

        // Meta (open-weight — self-hosted)
        Meta.Llama4Scout, Meta.Llama4Maverick,
        Meta.Llama3_3_70B, Meta.Llama3_2_3B, Meta.Llama3_2_1B,

        // Mistral
        Mistral.Large, Mistral.Small, Mistral.Nemo,

        // DeepSeek
        DeepSeek.V3, DeepSeek.R1
    ];

    // ─────────────────────────────────────────────────────────────
    //  Capability shorthands
    // ─────────────────────────────────────────────────────────────

    private const ModelCapability TextBase =
        ModelCapability.TextGeneration;

    private const ModelCapability ChatModel =
        TextBase | ModelCapability.StructuredOutput | ModelCapability.ToolCalling;

    private const ModelCapability FullModel =
        ChatModel | ModelCapability.CodeGeneration | ModelCapability.LargeContext;

    private const ModelCapability FrontierModel =
        FullModel | ModelCapability.Reasoning | ModelCapability.Vision;

    // ─────────────────────────────────────────────────────────────
    //  OpenAI
    // ─────────────────────────────────────────────────────────────

    /// <summary>OpenAI model templates (capabilities and context only — no pricing).</summary>
    public static class OpenAI
    {
        /// <summary>GPT-4.1 — flagship model with 1M context.</summary>
        public static ModelProfileTemplate Gpt4_1 { get; } = new()
        {
            Name = "gpt-4.1",
            Capabilities = FrontierModel,
            IntelligenceTier = 4,
            MaxContextTokens = 1_047_576,
            SpeedTier = 3
        };

        /// <summary>GPT-4.1 Mini — balanced cost/performance.</summary>
        public static ModelProfileTemplate Gpt4_1Mini { get; } = new()
        {
            Name = "gpt-4.1-mini",
            Capabilities = FullModel | ModelCapability.Vision,
            IntelligenceTier = 3,
            MaxContextTokens = 1_047_576,
            SpeedTier = 4
        };

        /// <summary>GPT-4.1 Nano — fastest, cheapest GPT-4.1 variant.</summary>
        public static ModelProfileTemplate Gpt4_1Nano { get; } = new()
        {
            Name = "gpt-4.1-nano",
            Capabilities = ChatModel | ModelCapability.LargeContext,
            IntelligenceTier = 2,
            MaxContextTokens = 1_047_576,
            SpeedTier = 5
        };

        /// <summary>o3 — frontier reasoning model.</summary>
        public static ModelProfileTemplate O3 { get; } = new()
        {
            Name = "o3",
            Capabilities = FrontierModel,
            IntelligenceTier = 5,
            MaxContextTokens = 200_000,
            SpeedTier = 1
        };

        /// <summary>o3-mini — fast reasoning.</summary>
        public static ModelProfileTemplate O3Mini { get; } = new()
        {
            Name = "o3-mini",
            Capabilities = FullModel | ModelCapability.Reasoning,
            IntelligenceTier = 4,
            MaxContextTokens = 200_000,
            SpeedTier = 3
        };

        /// <summary>o4-mini — latest compact reasoning model.</summary>
        public static ModelProfileTemplate O4Mini { get; } = new()
        {
            Name = "o4-mini",
            Capabilities = FrontierModel,
            IntelligenceTier = 4,
            MaxContextTokens = 200_000,
            SpeedTier = 3
        };

        /// <summary>GPT-4o — prior-gen frontier model.</summary>
        public static ModelProfileTemplate Gpt4o { get; } = new()
        {
            Name = "gpt-4o",
            Capabilities = FrontierModel | ModelCapability.AudioInput,
            IntelligenceTier = 4,
            MaxContextTokens = 128_000,
            SpeedTier = 3
        };

        /// <summary>GPT-4o Mini — prior-gen compact model.</summary>
        public static ModelProfileTemplate Gpt4oMini { get; } = new()
        {
            Name = "gpt-4o-mini",
            Capabilities = FullModel | ModelCapability.Vision,
            IntelligenceTier = 2,
            MaxContextTokens = 128_000,
            SpeedTier = 4
        };
    }

    // ─────────────────────────────────────────────────────────────
    //  Anthropic
    // ─────────────────────────────────────────────────────────────

    /// <summary>Anthropic Claude model templates.</summary>
    public static class Anthropic
    {
        /// <summary>Claude 4 Sonnet — balanced frontier model.</summary>
        public static ModelProfileTemplate Claude4Sonnet { get; } = new()
        {
            Name = "claude-sonnet-4-20250514",
            Capabilities = FrontierModel,
            IntelligenceTier = 4,
            MaxContextTokens = 200_000,
            SpeedTier = 3
        };

        /// <summary>Claude 4 Opus — highest-capability Anthropic model.</summary>
        public static ModelProfileTemplate Claude4Opus { get; } = new()
        {
            Name = "claude-opus-4-20250514",
            Capabilities = FrontierModel,
            IntelligenceTier = 5,
            MaxContextTokens = 200_000,
            SpeedTier = 1
        };

        /// <summary>Claude 3.7 Sonnet — prior-gen balanced model with extended thinking.</summary>
        public static ModelProfileTemplate Claude3_7Sonnet { get; } = new()
        {
            Name = "claude-3-7-sonnet-20250219",
            Capabilities = FrontierModel,
            IntelligenceTier = 4,
            MaxContextTokens = 200_000,
            SpeedTier = 3
        };

        /// <summary>Claude 3.5 Haiku — fastest Anthropic model.</summary>
        public static ModelProfileTemplate Claude3_5Haiku { get; } = new()
        {
            Name = "claude-3-5-haiku-20241022",
            Capabilities = ChatModel | ModelCapability.CodeGeneration | ModelCapability.Vision,
            IntelligenceTier = 2,
            MaxContextTokens = 200_000,
            SpeedTier = 5
        };

        /// <summary>Claude Opus 4.8 — current-generation frontier reasoning model.</summary>
        public static ModelProfileTemplate ClaudeOpus4_8 { get; } = new()
        {
            Name = "claude-opus-4-8",
            Capabilities = FrontierModel,
            IntelligenceTier = 5,
            MaxContextTokens = 1_000_000,
            SpeedTier = 2
        };

        /// <summary>Claude Sonnet 4.6 — current-generation balanced frontier model.</summary>
        public static ModelProfileTemplate ClaudeSonnet4_6 { get; } = new()
        {
            Name = "claude-sonnet-4-6",
            Capabilities = FrontierModel,
            IntelligenceTier = 4,
            MaxContextTokens = 1_000_000,
            SpeedTier = 3
        };

        /// <summary>Claude Haiku 4.5 — current-generation fast model (no Reasoning, by design).</summary>
        public static ModelProfileTemplate ClaudeHaiku4_5 { get; } = new()
        {
            Name = "claude-haiku-4-5",
            Capabilities = FullModel | ModelCapability.Vision,
            IntelligenceTier = 3,
            MaxContextTokens = 200_000,
            SpeedTier = 5
        };
    }

    // ─────────────────────────────────────────────────────────────
    //  Google
    // ─────────────────────────────────────────────────────────────

    /// <summary>Google Gemini model templates.</summary>
    public static class Google
    {
        /// <summary>Gemini 3.1 Pro — Agent Platform GA flagship, frontier reasoning with 2M context.</summary>
        public static ModelProfileTemplate Gemini3_1Pro { get; } = new()
        {
            Name = "gemini-3.1-pro",
            Capabilities = FrontierModel | ModelCapability.AudioInput | ModelCapability.VideoInput,
            IntelligenceTier = 5,
            MaxContextTokens = 2_097_152,
            SpeedTier = 2
        };

        /// <summary>Gemini 3.1 Flash — Agent Platform GA fast model with reasoning and multimodal input.</summary>
        public static ModelProfileTemplate Gemini3_1Flash { get; } = new()
        {
            Name = "gemini-3.1-flash",
            Capabilities = FullModel | ModelCapability.Reasoning | ModelCapability.Vision
                         | ModelCapability.AudioInput | ModelCapability.VideoInput,
            IntelligenceTier = 4,
            MaxContextTokens = 1_048_576,
            SpeedTier = 4
        };

        /// <summary>Gemini 2.5 Pro — frontier reasoning model with 1M context.</summary>
        public static ModelProfileTemplate Gemini2_5Pro { get; } = new()
        {
            Name = "gemini-2.5-pro",
            Capabilities = FrontierModel | ModelCapability.AudioInput | ModelCapability.VideoInput,
            IntelligenceTier = 5,
            MaxContextTokens = 1_048_576,
            SpeedTier = 2
        };

        /// <summary>Gemini 2.5 Flash — fast, cost-effective with thinking.</summary>
        public static ModelProfileTemplate Gemini2_5Flash { get; } = new()
        {
            Name = "gemini-2.5-flash",
            Capabilities = FullModel | ModelCapability.Reasoning | ModelCapability.Vision
                         | ModelCapability.AudioInput | ModelCapability.VideoInput,
            IntelligenceTier = 3,
            MaxContextTokens = 1_048_576,
            SpeedTier = 4
        };

        /// <summary>Gemini 2.0 Flash — prior-gen fast model.</summary>
        public static ModelProfileTemplate Gemini2_0Flash { get; } = new()
        {
            Name = "gemini-2.0-flash",
            Capabilities = FullModel | ModelCapability.Vision | ModelCapability.AudioInput,
            IntelligenceTier = 3,
            MaxContextTokens = 1_048_576,
            SpeedTier = 4
        };
    }

    // ─────────────────────────────────────────────────────────────
    //  Meta (open-weight — self-hosted)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Meta Llama model templates for local / self-hosted inference
    /// (Ollama, llama.cpp, vLLM, TGI). Use <c>ToProfile(model)</c> for
    /// zero-cost, or <c>ToProfile(model, rates)</c> if running on a paid provider.
    /// </summary>
    public static class Meta
    {
        /// <summary>Llama 4 Scout — 17B active params, MoE, 10M context.</summary>
        public static ModelProfileTemplate Llama4Scout { get; } = new()
        {
            Name = "llama-4-scout",
            Capabilities = FullModel | ModelCapability.Vision,
            IntelligenceTier = 3,
            MaxContextTokens = 10_000_000,
            SpeedTier = 3
        };

        /// <summary>Llama 4 Maverick — 17B active params, MoE, 1M context.</summary>
        public static ModelProfileTemplate Llama4Maverick { get; } = new()
        {
            Name = "llama-4-maverick",
            Capabilities = FullModel | ModelCapability.Vision,
            IntelligenceTier = 4,
            MaxContextTokens = 1_048_576,
            SpeedTier = 2
        };

        /// <summary>Llama 3.3 70B — strong open-weight model.</summary>
        public static ModelProfileTemplate Llama3_3_70B { get; } = new()
        {
            Name = "llama-3.3-70b",
            Capabilities = ChatModel | ModelCapability.CodeGeneration,
            IntelligenceTier = 3,
            MaxContextTokens = 128_000,
            SpeedTier = 2
        };

        /// <summary>Llama 3.2 3B — compact, fast local model.</summary>
        public static ModelProfileTemplate Llama3_2_3B { get; } = new()
        {
            Name = "llama-3.2-3b",
            Capabilities = TextBase | ModelCapability.StructuredOutput,
            IntelligenceTier = 1,
            MaxContextTokens = 128_000,
            SpeedTier = 5
        };

        /// <summary>Llama 3.2 1B — ultra-light edge model.</summary>
        public static ModelProfileTemplate Llama3_2_1B { get; } = new()
        {
            Name = "llama-3.2-1b",
            Capabilities = TextBase,
            IntelligenceTier = 1,
            MaxContextTokens = 128_000,
            SpeedTier = 5
        };
    }

    // ─────────────────────────────────────────────────────────────
    //  Mistral
    // ─────────────────────────────────────────────────────────────

    /// <summary>Mistral AI model templates.</summary>
    public static class Mistral
    {
        /// <summary>Mistral Large — flagship model.</summary>
        public static ModelProfileTemplate Large { get; } = new()
        {
            Name = "mistral-large-latest",
            Capabilities = FrontierModel,
            IntelligenceTier = 4,
            MaxContextTokens = 128_000,
            SpeedTier = 3
        };

        /// <summary>Mistral Small — cost-effective for most tasks.</summary>
        public static ModelProfileTemplate Small { get; } = new()
        {
            Name = "mistral-small-latest",
            Capabilities = ChatModel | ModelCapability.CodeGeneration | ModelCapability.Vision,
            IntelligenceTier = 2,
            MaxContextTokens = 128_000,
            SpeedTier = 4
        };

        /// <summary>Mistral Nemo — 12B open-weight, fast local inference.</summary>
        public static ModelProfileTemplate Nemo { get; } = new()
        {
            Name = "mistral-nemo",
            Capabilities = ChatModel | ModelCapability.CodeGeneration,
            IntelligenceTier = 2,
            MaxContextTokens = 128_000,
            SpeedTier = 4
        };
    }

    // ─────────────────────────────────────────────────────────────
    //  DeepSeek
    // ─────────────────────────────────────────────────────────────

    /// <summary>DeepSeek model templates.</summary>
    public static class DeepSeek
    {
        /// <summary>DeepSeek V3 — MoE general-purpose model.</summary>
        public static ModelProfileTemplate V3 { get; } = new()
        {
            Name = "deepseek-chat",
            Capabilities = FullModel,
            IntelligenceTier = 3,
            MaxContextTokens = 128_000,
            SpeedTier = 3
        };

        /// <summary>DeepSeek R1 — reasoning-focused model.</summary>
        public static ModelProfileTemplate R1 { get; } = new()
        {
            Name = "deepseek-reasoner",
            Capabilities = FullModel | ModelCapability.Reasoning,
            IntelligenceTier = 4,
            MaxContextTokens = 128_000,
            SpeedTier = 2
        };
    }
}
