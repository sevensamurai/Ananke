namespace Ananke.Orchestration.Agents;

/// <summary>
/// Flags describing discrete model skills, ordered from least to most demanding.
/// Combine flags to express compound requirements. Higher bit positions represent
/// capabilities that typically require more capable (and expensive) models.
/// Used by <see cref="ModelProfile"/> and <see cref="TaskRequirements"/> for capability-based routing.
/// </summary>
[Flags]
public enum ModelCapability
{
    /// <summary>No specific capability.</summary>
    None = 0,

    // ── Tier 1 — Basic: available on nearly every model ──

    /// <summary>General text generation and chat.</summary>
    TextGeneration = 1 << 0,

    /// <summary>Large context window (&gt; 32 K tokens).</summary>
    LargeContext = 1 << 1,

    // ── Tier 2 — Intermediate: requires mid-range models ──

    /// <summary>JSON structured output with schema adherence.</summary>
    StructuredOutput = 1 << 2,

    /// <summary>Function / tool-calling support.</summary>
    ToolCalling = 1 << 3,

    // ── Tier 3 — Advanced: requires capable models ──

    /// <summary>Code authoring, review, and debugging.</summary>
    CodeGeneration = 1 << 4,

    /// <summary>Image or multi-modal input understanding.</summary>
    Vision = 1 << 5,

    // ── Tier 4 — Frontier: requires the most capable models ──

    /// <summary>Multi-step reasoning, chain-of-thought, and complex analysis.</summary>
    Reasoning = 1 << 6,
}
