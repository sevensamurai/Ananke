namespace Ananke.Abstractions.Agents;

/// <summary>
/// Known model identifiers for each provider. These are the <b>shortest strings
/// the provider SDK actually accepts</b> — no date suffixes, no aliases.
/// </summary>
/// <remarks>
/// <para>
/// Use these constants in manifests (<c>model:</c> field), model mappers,
/// and anywhere a model identifier is needed. The provider SDK resolves
/// each to the latest point release automatically.
/// </para>
/// <para>
/// Pinned, date-suffixed versions of a model (e.g. <c>{model}-YYYYMMDD</c>) still work as
/// passthrough — these constants represent the <b>recommended default</b>, not the only valid
/// string. A pin only stays valid as long as the provider keeps serving that exact snapshot; see
/// <see href="https://github.com/sevensamurai/Ananke/blob/main/docs/reference/model-deprecations.md"/>
/// before relying on one long-term.
/// </para>
/// <para>
/// Constants marked <see cref="ObsoleteAttribute"/> are <b>Deprecated</b> — still callable, but
/// superseded. See <see href="https://github.com/sevensamurai/Ananke/blob/main/docs/reference/model-deprecations.md"/>
/// for the full lifecycle table and policy.
/// </para>
/// </remarks>
public static class Models
{
    // Every constant in this file necessarily assigns its own literal id as its value — for a
    // Deprecated constant, that literal always matches model-lifecycle.json by construction.
    // ANNKE002 exists to catch a deprecated literal showing up somewhere else in the codebase
    // (a mapper table, a manifest example, a hardcoded default), not the one place a deprecated
    // id is *expected* to appear: right here, as the constant's own value.
#pragma warning disable ANNKE002

    private const string DeprecationDocs =
        "https://github.com/sevensamurai/Ananke/blob/main/docs/reference/model-deprecations.md#{0}";

    /// <summary>OpenAI model identifiers. Already short — these are the wire names.</summary>
    public static class OpenAI
    {
        /// <summary>GPT-4.1 — flagship reasoning model.</summary>
        [Obsolete("gpt-4.1 is deprecated; use Models.OpenAI.Gpt56Sol.",
            DiagnosticId = "ANNKE001", UrlFormat = DeprecationDocs)]
        public const string Gpt41 = "gpt-4.1";

        /// <summary>GPT-4.1 Mini — fast, cost-efficient.</summary>
        [Obsolete("gpt-4.1-mini is deprecated; use Models.OpenAI.Gpt56Terra.",
            DiagnosticId = "ANNKE001", UrlFormat = DeprecationDocs)]
        public const string Gpt41Mini = "gpt-4.1-mini";

        /// <summary>GPT-4.1 Nano — smallest, cheapest.</summary>
        [Obsolete("gpt-4.1-nano is deprecated; use Models.OpenAI.Gpt56Luna.",
            DiagnosticId = "ANNKE001", UrlFormat = DeprecationDocs)]
        public const string Gpt41Nano = "gpt-4.1-nano";

        /// <summary>GPT-4o — multimodal flagship (prior generation).</summary>
        [Obsolete("gpt-4o is deprecated; use Models.OpenAI.Gpt56Sol.",
            DiagnosticId = "ANNKE001", UrlFormat = DeprecationDocs)]
        public const string Gpt4o = "gpt-4o";

        /// <summary>GPT-4o Mini — fast multimodal (prior generation).</summary>
        [Obsolete("gpt-4o-mini is deprecated; use Models.OpenAI.Gpt56Terra.",
            DiagnosticId = "ANNKE001", UrlFormat = DeprecationDocs)]
        public const string Gpt4oMini = "gpt-4o-mini";

        /// <summary>o3 — reasoning model.</summary>
        [Obsolete("o3 is deprecated; use Models.OpenAI.Gpt56Sol.",
            DiagnosticId = "ANNKE001", UrlFormat = DeprecationDocs)]
        public const string O3 = "o3";

        /// <summary>o3-mini — compact reasoning.</summary>
        [Obsolete("o3-mini is deprecated; use Models.OpenAI.Gpt56Terra.",
            DiagnosticId = "ANNKE001", UrlFormat = DeprecationDocs)]
        public const string O3Mini = "o3-mini";

        /// <summary>o4-mini — latest compact reasoning.</summary>
        [Obsolete("o4-mini is deprecated; use Models.OpenAI.Gpt56Terra.",
            DiagnosticId = "ANNKE001", UrlFormat = DeprecationDocs)]
        public const string O4Mini = "o4-mini";

        /// <summary>GPT-5 — prior generation, reasoning and coding.</summary>
        [Obsolete("gpt-5 is deprecated; use Models.OpenAI.Gpt56Sol.",
            DiagnosticId = "ANNKE001", UrlFormat = DeprecationDocs)]
        public const string Gpt5 = "gpt-5";

        /// <summary>GPT-5 Mini — prior generation, fast and cost-efficient.</summary>
        [Obsolete("gpt-5-mini is deprecated; use Models.OpenAI.Gpt56Terra.",
            DiagnosticId = "ANNKE001", UrlFormat = DeprecationDocs)]
        public const string Gpt5Mini = "gpt-5-mini";

        /// <summary>GPT-5 Nano — prior generation, smallest and cheapest.</summary>
        [Obsolete("gpt-5-nano is deprecated; use Models.OpenAI.Gpt56Luna.",
            DiagnosticId = "ANNKE001", UrlFormat = DeprecationDocs)]
        public const string Gpt5Nano = "gpt-5-nano";

        /// <summary>GPT-5.2 — legacy incremental update, two generations behind current.</summary>
        [Obsolete("gpt-5.2 is deprecated; use Models.OpenAI.Gpt56Sol.",
            DiagnosticId = "ANNKE001", UrlFormat = DeprecationDocs)]
        public const string Gpt52 = "gpt-5.2";

        /// <summary>GPT-5.4 — legacy, 1M-class context, still fully supported.</summary>
        public const string Gpt54 = "gpt-5.4";

        /// <summary>GPT-5.4 Mini — legacy, fast distillation of GPT-5.4.</summary>
        public const string Gpt54Mini = "gpt-5.4-mini";

        /// <summary>GPT-5.4 Nano — legacy, fastest and cheapest of the 5.4 line.</summary>
        public const string Gpt54Nano = "gpt-5.4-nano";

        /// <summary>GPT-5.5 — legacy flagship, complex reasoning and coding.</summary>
        public const string Gpt55 = "gpt-5.5";

        /// <summary>GPT-5.6 Sol — current generation flagship: frontier reasoning across coding, research, and agentic/computer-use work.</summary>
        public const string Gpt56Sol = "gpt-5.6-sol";

        /// <summary>GPT-5.6 Terra — current generation, balances capability and cost for everyday tasks.</summary>
        public const string Gpt56Terra = "gpt-5.6-terra";

        /// <summary>GPT-5.6 Luna — current generation, fastest and lowest-cost of the 5.6 line.</summary>
        public const string Gpt56Luna = "gpt-5.6-luna";
    }

    /// <summary>
    /// Anthropic Claude model identifiers. The SDK accepts these without
    /// the date suffix (e.g. <c>claude-sonnet-4</c> resolves to the latest
    /// point release of Sonnet 4).
    /// </summary>
    public static class Anthropic
    {
        // Opus4, Sonnet4 (claude-opus-4-20250514 / claude-sonnet-4-20250514, retired 2026-06-15),
        // Sonnet35 (claude-3-5-sonnet-*, retired 2025-10-28), and Haiku35 (claude-3-5-haiku-20241022,
        // retired 2026-02-19) were removed — the provider no longer serves these; keeping them as
        // constants (even Retired-status ones) would let new code reference an always-failing model.
        // See docs/reference/model-deprecations.md.

        /// <summary>Claude Opus 4.1 — legacy, deprecated 2026-06-05, retires 2026-08-05 (provider-confirmed).</summary>
        [Obsolete("claude-opus-4-1 is deprecated; use Models.Anthropic.Opus48.",
            DiagnosticId = "ANNKE001", UrlFormat = DeprecationDocs)]
        public const string Opus41 = "claude-opus-4-1";

        /// <summary>Claude Opus 4.8 — legacy, most capable of its generation, still fully supported.</summary>
        public const string Opus48 = "claude-opus-4-8";

        /// <summary>Claude Sonnet 4.6 — legacy, balanced performance and speed, still fully supported.</summary>
        public const string Sonnet46 = "claude-sonnet-4-6";

        /// <summary>Claude Haiku 4.5 — current generation, fastest, most compact.</summary>
        public const string Haiku45 = "claude-haiku-4-5";

        /// <summary>Claude Sonnet 5 — current generation, balanced performance and speed.</summary>
        public const string Sonnet5 = "claude-sonnet-5";

        /// <summary>Claude Opus 5 — current generation, complex agentic coding and enterprise work.</summary>
        public const string Opus5 = "claude-opus-5";

        /// <summary>Claude Fable 5 — Mythos-class frontier model, most capable, complex reasoning.</summary>
        public const string Fable5 = "claude-fable-5";
    }

    /// <summary>Google Gemini model identifiers. Already short wire names.</summary>
    public static class Google
    {
        /// <summary>Gemini 3.1 Pro — Agent Platform GA flagship, frontier reasoning.</summary>
        public const string Gemini31Pro = "gemini-3.1-pro";

        /// <summary>Gemini 3.1 Flash — legacy, superseded by Gemini 3.5 Flash, still fully supported.</summary>
        public const string Gemini31Flash = "gemini-3.1-flash";

        /// <summary>Gemini 3.1 Flash Image — multimodal variant with image generation.</summary>
        public const string Gemini31FlashImage = "gemini-3.1-flash-image";

        /// <summary>Gemini 3.1 Flash-Lite — legacy, superseded by Gemini 3.5 Flash-Lite, still fully supported.</summary>
        public const string Gemini31FlashLite = "gemini-3.1-flash-lite";

        /// <summary>Gemini 3.5 Flash — legacy, superseded by Gemini 3.6 Flash, still fully supported.</summary>
        public const string Gemini35Flash = "gemini-3.5-flash";

        /// <summary>
        /// Gemini 3.5 Flash-Lite — current generation, most cost-effective Gemini model, high-throughput.
        /// Context window/latency not independently confirmed beyond the models-page listing; carried
        /// forward from Gemini 3.1 Flash-Lite's spec as the closest known baseline.
        /// </summary>
        public const string Gemini35FlashLite = "gemini-3.5-flash-lite";

        /// <summary>
        /// Gemini 3.6 Flash — current generation, frontier-level agentic and coding performance.
        /// Context window/latency not independently confirmed beyond the models-page listing; carried
        /// forward from Gemini 3.5 Flash's spec as the closest known baseline.
        /// </summary>
        public const string Gemini36Flash = "gemini-3.6-flash";

        /// <summary>Gemma 4 — open-weight model available via Agent Platform / Model Garden.</summary>
        public const string Gemma4 = "gemma-4";

        /// <summary>Lyria 3 — Google DeepMind music / audio generation model.</summary>
        public const string Lyria3 = "lyria-3";

        /// <summary>Gemini 2.5 Pro — flagship, complex tasks.</summary>
        [Obsolete("gemini-2.5-pro is deprecated; use Models.Google.Gemini31Pro.",
            DiagnosticId = "ANNKE001", UrlFormat = DeprecationDocs)]
        public const string Gemini25Pro = "gemini-2.5-pro";

        /// <summary>Gemini 2.5 Flash — fast, balanced.</summary>
        [Obsolete("gemini-2.5-flash is deprecated; use Models.Google.Gemini35Flash.",
            DiagnosticId = "ANNKE001", UrlFormat = DeprecationDocs)]
        public const string Gemini25Flash = "gemini-2.5-flash";

        // Gemini20Flash and Gemini20FlashLite (gemini-2.0-flash / -lite, shutdown 2026-06-01) were
        // removed — already past their shutdown date. See docs/reference/model-deprecations.md.
    }

#pragma warning restore ANNKE002
}
