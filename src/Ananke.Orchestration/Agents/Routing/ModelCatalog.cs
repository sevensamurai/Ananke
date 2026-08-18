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

    /// <summary>Lifecycle stage of this template. Carried into <see cref="ToProfile(IAgentModel, ModelCostRates)"/>.</summary>
    public ModelStatus Status { get; init; } = ModelStatus.Current;

    /// <summary>Recommended replacement model name when <see cref="Status"/> is not <see cref="ModelStatus.Current"/>.</summary>
    public string? ReplacedBy { get; init; }

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
            SpeedTier = SpeedTier,
            Status = Status,
            ReplacedBy = ReplacedBy
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
        OpenAI.Gpt5, OpenAI.Gpt5Mini, OpenAI.Gpt5Nano, OpenAI.Gpt52,
        OpenAI.Gpt54, OpenAI.Gpt54Mini, OpenAI.Gpt54Nano, OpenAI.Gpt55,
        OpenAI.Gpt56Sol, OpenAI.Gpt56Terra, OpenAI.Gpt56Luna,

        // Anthropic
        Anthropic.ClaudeOpus4_8, Anthropic.ClaudeSonnet4_6, Anthropic.ClaudeHaiku4_5,
        Anthropic.ClaudeSonnet5, Anthropic.ClaudeOpus5, Anthropic.ClaudeFable5,

        // Google
        Google.Gemini3_1Pro, Google.Gemini3_1Flash,
        Google.Gemini2_5Pro, Google.Gemini2_5Flash,
        Google.Gemini3_5Flash, Google.Gemini3_1FlashLite,
        Google.Gemini3_6Flash, Google.Gemini3_5FlashLite,

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

    /// <summary>
    /// Builds a template with its <see cref="ModelProfileTemplate.Status"/> and
    /// <see cref="ModelProfileTemplate.ReplacedBy"/> looked up from
    /// <see cref="ModelLifecycleData"/> by <paramref name="name"/> — the same
    /// single-source-of-truth data <c>Ananke.Design.ModelCatalog</c> and the <c>ANNKE002</c>
    /// analyzer read, so a template's lifecycle can't drift from theirs by hand-editing one and
    /// not the others. A <paramref name="name"/> absent from the data is
    /// <see cref="ModelStatus.Current"/>.
    /// </summary>
    private static ModelProfileTemplate CreateTemplate(
        string name, ModelCapability capabilities, int intelligenceTier, int maxContextTokens, int speedTier)
    {
        var (status, replacedBy) = ModelLifecycleData.Entries.TryGetValue(name, out var entry)
            ? (entry.Status, entry.ReplacedBy)
            : (ModelStatus.Current, null);

        return new ModelProfileTemplate
        {
            Name = name,
            Capabilities = capabilities,
            IntelligenceTier = intelligenceTier,
            MaxContextTokens = maxContextTokens,
            SpeedTier = speedTier,
            Status = status,
            ReplacedBy = replacedBy
        };
    }

    // ─────────────────────────────────────────────────────────────
    //  OpenAI
    // ─────────────────────────────────────────────────────────────

    /// <summary>OpenAI model templates (capabilities and context only — no pricing).</summary>
    public static class OpenAI
    {
        // This catalog must keep resolving deprecated model constants for its own Name
        // assignments below — ANNKE001 is expected and intentional here, not a call site to fix.
#pragma warning disable ANNKE001

        /// <summary>GPT-4.1 — flagship model with 1M context.</summary>
        public static ModelProfileTemplate Gpt4_1 { get; } =
            CreateTemplate(Models.OpenAI.Gpt41, FrontierModel, intelligenceTier: 4, maxContextTokens: 1_047_576, speedTier: 3);

        /// <summary>GPT-4.1 Mini — balanced cost/performance.</summary>
        public static ModelProfileTemplate Gpt4_1Mini { get; } =
            CreateTemplate(Models.OpenAI.Gpt41Mini, FullModel | ModelCapability.Vision, intelligenceTier: 3, maxContextTokens: 1_047_576, speedTier: 4);

        /// <summary>GPT-4.1 Nano — fastest, cheapest GPT-4.1 variant.</summary>
        public static ModelProfileTemplate Gpt4_1Nano { get; } =
            CreateTemplate(Models.OpenAI.Gpt41Nano, ChatModel | ModelCapability.LargeContext, intelligenceTier: 2, maxContextTokens: 1_047_576, speedTier: 5);

        /// <summary>o3 — frontier reasoning model.</summary>
        public static ModelProfileTemplate O3 { get; } =
            CreateTemplate(Models.OpenAI.O3, FrontierModel, intelligenceTier: 5, maxContextTokens: 200_000, speedTier: 1);

        /// <summary>o3-mini — fast reasoning.</summary>
        public static ModelProfileTemplate O3Mini { get; } =
            CreateTemplate(Models.OpenAI.O3Mini, FullModel | ModelCapability.Reasoning, intelligenceTier: 4, maxContextTokens: 200_000, speedTier: 3);

        /// <summary>o4-mini — latest compact reasoning model.</summary>
        public static ModelProfileTemplate O4Mini { get; } =
            CreateTemplate(Models.OpenAI.O4Mini, FrontierModel, intelligenceTier: 4, maxContextTokens: 200_000, speedTier: 3);

        /// <summary>GPT-4o — prior-gen frontier model.</summary>
        public static ModelProfileTemplate Gpt4o { get; } =
            CreateTemplate(Models.OpenAI.Gpt4o, FrontierModel | ModelCapability.AudioInput, intelligenceTier: 4, maxContextTokens: 128_000, speedTier: 3);

        /// <summary>GPT-4o Mini — prior-gen compact model.</summary>
        public static ModelProfileTemplate Gpt4oMini { get; } =
            CreateTemplate(Models.OpenAI.Gpt4oMini, FullModel | ModelCapability.Vision, intelligenceTier: 2, maxContextTokens: 128_000, speedTier: 4);

        /// <summary>GPT-5 — prior-gen reasoning and coding model.</summary>
        public static ModelProfileTemplate Gpt5 { get; } =
            CreateTemplate(Models.OpenAI.Gpt5, FrontierModel, intelligenceTier: 4, maxContextTokens: 400_000, speedTier: 3);

        /// <summary>GPT-5 Mini — prior-gen fast, cost-efficient reasoning.</summary>
        public static ModelProfileTemplate Gpt5Mini { get; } =
            CreateTemplate(Models.OpenAI.Gpt5Mini, FullModel | ModelCapability.Reasoning, intelligenceTier: 3, maxContextTokens: 400_000, speedTier: 4);

        /// <summary>GPT-5 Nano — prior-gen smallest, cheapest.</summary>
        public static ModelProfileTemplate Gpt5Nano { get; } =
            CreateTemplate(Models.OpenAI.Gpt5Nano, FullModel | ModelCapability.Reasoning, intelligenceTier: 2, maxContextTokens: 400_000, speedTier: 5);

        /// <summary>GPT-5.2 — prior-gen incremental update over GPT-5.</summary>
        public static ModelProfileTemplate Gpt52 { get; } =
            CreateTemplate(Models.OpenAI.Gpt52, FrontierModel, intelligenceTier: 4, maxContextTokens: 400_000, speedTier: 3);

        /// <summary>GPT-5.4 — legacy, 1M-class context.</summary>
        public static ModelProfileTemplate Gpt54 { get; } =
            CreateTemplate(Models.OpenAI.Gpt54, FrontierModel, intelligenceTier: 5, maxContextTokens: 1_050_000, speedTier: 2);

        /// <summary>GPT-5.4 Mini — legacy, fast distillation of GPT-5.4.</summary>
        public static ModelProfileTemplate Gpt54Mini { get; } =
            CreateTemplate(Models.OpenAI.Gpt54Mini, FullModel | ModelCapability.Reasoning, intelligenceTier: 4, maxContextTokens: 400_000, speedTier: 4);

        /// <summary>GPT-5.4 Nano — legacy, fastest, cheapest of the 5.4 line.</summary>
        public static ModelProfileTemplate Gpt54Nano { get; } =
            CreateTemplate(Models.OpenAI.Gpt54Nano, FullModel | ModelCapability.Reasoning, intelligenceTier: 3, maxContextTokens: 400_000, speedTier: 5);

        /// <summary>GPT-5.5 — legacy flagship for complex reasoning and coding.</summary>
        public static ModelProfileTemplate Gpt55 { get; } =
            CreateTemplate(Models.OpenAI.Gpt55, FrontierModel, intelligenceTier: 5, maxContextTokens: 1_000_000, speedTier: 2);

        /// <summary>GPT-5.6 Sol — current-gen flagship: frontier reasoning for coding, research, and agentic/computer-use work, 1.05M-class context.</summary>
        public static ModelProfileTemplate Gpt56Sol { get; } =
            CreateTemplate(Models.OpenAI.Gpt56Sol, FrontierModel, intelligenceTier: 5, maxContextTokens: 1_050_000, speedTier: 2);

        /// <summary>GPT-5.6 Terra — current-gen, balances capability and cost (mini-tier equivalent).</summary>
        public static ModelProfileTemplate Gpt56Terra { get; } =
            CreateTemplate(Models.OpenAI.Gpt56Terra, FullModel | ModelCapability.Reasoning, intelligenceTier: 4, maxContextTokens: 1_050_000, speedTier: 4);

        /// <summary>GPT-5.6 Luna — current-gen fastest, lowest-cost of the 5.6 line (nano-tier equivalent).</summary>
        public static ModelProfileTemplate Gpt56Luna { get; } =
            CreateTemplate(Models.OpenAI.Gpt56Luna, FullModel | ModelCapability.Reasoning, intelligenceTier: 3, maxContextTokens: 1_050_000, speedTier: 5);

#pragma warning restore ANNKE001
    }

    // ─────────────────────────────────────────────────────────────
    //  Anthropic
    // ─────────────────────────────────────────────────────────────

    /// <summary>Anthropic Claude model templates.</summary>
    public static class Anthropic
    {
        // Claude4Sonnet/Claude4Opus (claude-sonnet-4/claude-opus-4, backed by the sole snapshot
        // claude-*-4-20250514, retired 2026-06-15), Claude3_7Sonnet (claude-3-7-sonnet-20250219,
        // retired 2026-02-19), and Claude3_5Haiku (claude-3-5-haiku-20241022, retired 2026-02-19)
        // were removed — the provider no longer serves these. See
        // docs/reference/model-deprecations.md.

        // This catalog must keep resolving deprecated model constants for its own Name
        // assignments below — ANNKE001 is expected and intentional here, not a call site to fix.
#pragma warning disable ANNKE001

        /// <summary>Claude Opus 4.8 — prior-generation frontier reasoning model.</summary>
        public static ModelProfileTemplate ClaudeOpus4_8 { get; } =
            CreateTemplate(Models.Anthropic.Opus48, FrontierModel, intelligenceTier: 5, maxContextTokens: 1_000_000, speedTier: 2);

        /// <summary>Claude Sonnet 4.6 — prior-generation balanced frontier model.</summary>
        public static ModelProfileTemplate ClaudeSonnet4_6 { get; } =
            CreateTemplate(Models.Anthropic.Sonnet46, FrontierModel, intelligenceTier: 4, maxContextTokens: 1_000_000, speedTier: 3);

        /// <summary>Claude Haiku 4.5 — current-generation fast model (no Reasoning, by design).</summary>
        public static ModelProfileTemplate ClaudeHaiku4_5 { get; } =
            CreateTemplate(Models.Anthropic.Haiku45, FullModel | ModelCapability.Vision, intelligenceTier: 3, maxContextTokens: 200_000, speedTier: 5);

        /// <summary>Claude Sonnet 5 — current-generation balanced frontier model.</summary>
        public static ModelProfileTemplate ClaudeSonnet5 { get; } =
            CreateTemplate(Models.Anthropic.Sonnet5, FrontierModel, intelligenceTier: 4, maxContextTokens: 1_000_000, speedTier: 3);

        /// <summary>Claude Opus 5 — current-generation, complex agentic coding and enterprise work.</summary>
        public static ModelProfileTemplate ClaudeOpus5 { get; } =
            CreateTemplate(Models.Anthropic.Opus5, FrontierModel, intelligenceTier: 5, maxContextTokens: 1_000_000, speedTier: 2);

        /// <summary>Claude Fable 5 — Mythos-class frontier model, most capable, complex reasoning.</summary>
        public static ModelProfileTemplate ClaudeFable5 { get; } =
            CreateTemplate(Models.Anthropic.Fable5, FrontierModel, intelligenceTier: 5, maxContextTokens: 1_000_000, speedTier: 2);

#pragma warning restore ANNKE001
    }

    // ─────────────────────────────────────────────────────────────
    //  Google
    // ─────────────────────────────────────────────────────────────

    /// <summary>Google Gemini model templates.</summary>
    public static class Google
    {
        // This catalog must keep resolving deprecated model constants for its own Name
        // assignments below — ANNKE001 is expected and intentional here, not a call site to fix.
#pragma warning disable ANNKE001

        /// <summary>Gemini 3.1 Pro — Agent Platform GA flagship, frontier reasoning with 2M context.</summary>
        public static ModelProfileTemplate Gemini3_1Pro { get; } =
            CreateTemplate(Models.Google.Gemini31Pro, FrontierModel | ModelCapability.AudioInput | ModelCapability.VideoInput, intelligenceTier: 5, maxContextTokens: 2_097_152, speedTier: 2);

        /// <summary>Gemini 3.1 Flash — Agent Platform GA fast model with reasoning and multimodal input.</summary>
        public static ModelProfileTemplate Gemini3_1Flash { get; } =
            CreateTemplate(Models.Google.Gemini31Flash,
                FullModel | ModelCapability.Reasoning | ModelCapability.Vision | ModelCapability.AudioInput | ModelCapability.VideoInput,
                intelligenceTier: 4, maxContextTokens: 1_048_576, speedTier: 4);

        /// <summary>Gemini 2.5 Pro — frontier reasoning model with 1M context.</summary>
        public static ModelProfileTemplate Gemini2_5Pro { get; } =
            CreateTemplate(Models.Google.Gemini25Pro, FrontierModel | ModelCapability.AudioInput | ModelCapability.VideoInput, intelligenceTier: 5, maxContextTokens: 1_048_576, speedTier: 2);

        /// <summary>Gemini 2.5 Flash — fast, cost-effective with thinking.</summary>
        public static ModelProfileTemplate Gemini2_5Flash { get; } =
            CreateTemplate(Models.Google.Gemini25Flash,
                FullModel | ModelCapability.Reasoning | ModelCapability.Vision | ModelCapability.AudioInput | ModelCapability.VideoInput,
                intelligenceTier: 3, maxContextTokens: 1_048_576, speedTier: 4);

        // Gemini2_0Flash (gemini-2.0-flash, shutdown 2026-06-01) was removed — already past its
        // shutdown date. See docs/reference/model-deprecations.md.

        /// <summary>Gemini 3.5 Flash — legacy, superseded by Gemini 3.6 Flash, still fully supported.</summary>
        public static ModelProfileTemplate Gemini3_5Flash { get; } =
            CreateTemplate(Models.Google.Gemini35Flash, FrontierModel | ModelCapability.AudioInput | ModelCapability.VideoInput, intelligenceTier: 5, maxContextTokens: 1_000_000, speedTier: 4);

        /// <summary>Gemini 3.1 Flash-Lite — legacy, superseded by Gemini 3.5 Flash-Lite, still fully supported.</summary>
        public static ModelProfileTemplate Gemini3_1FlashLite { get; } =
            CreateTemplate(Models.Google.Gemini31FlashLite,
                FullModel | ModelCapability.Vision | ModelCapability.AudioInput | ModelCapability.VideoInput,
                intelligenceTier: 2, maxContextTokens: 1_000_000, speedTier: 5);

        /// <summary>
        /// Gemini 3.6 Flash — current-gen, frontier-level agentic and coding performance. Context/speed
        /// carried forward from Gemini 3.5 Flash's spec — not independently confirmed beyond the
        /// models-page name listing.
        /// </summary>
        public static ModelProfileTemplate Gemini3_6Flash { get; } =
            CreateTemplate(Models.Google.Gemini36Flash, FrontierModel | ModelCapability.AudioInput | ModelCapability.VideoInput, intelligenceTier: 5, maxContextTokens: 1_000_000, speedTier: 4);

        /// <summary>
        /// Gemini 3.5 Flash-Lite — current-gen, most cost-effective Gemini model, high-throughput.
        /// Context/speed carried forward from Gemini 3.1 Flash-Lite's spec — not independently
        /// confirmed beyond the models-page name listing.
        /// </summary>
        public static ModelProfileTemplate Gemini3_5FlashLite { get; } =
            CreateTemplate(Models.Google.Gemini35FlashLite,
                FullModel | ModelCapability.Vision | ModelCapability.AudioInput | ModelCapability.VideoInput,
                intelligenceTier: 2, maxContextTokens: 1_000_000, speedTier: 5);

#pragma warning restore ANNKE001
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
