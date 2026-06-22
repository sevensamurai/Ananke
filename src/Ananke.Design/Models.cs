namespace Ananke.Design;

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
/// Pinned versions (e.g. <c>claude-sonnet-4-20250514</c>) still work as
/// passthrough — these constants represent the <b>recommended default</b>.
/// </para>
/// </remarks>
public static class Models
{
    /// <summary>OpenAI model identifiers. Already short — these are the wire names.</summary>
    public static class OpenAI
    {
        /// <summary>GPT-4.1 — flagship reasoning model.</summary>
        public const string Gpt41 = "gpt-4.1";

        /// <summary>GPT-4.1 Mini — fast, cost-efficient.</summary>
        public const string Gpt41Mini = "gpt-4.1-mini";

        /// <summary>GPT-4.1 Nano — smallest, cheapest.</summary>
        public const string Gpt41Nano = "gpt-4.1-nano";

        /// <summary>GPT-4o — multimodal flagship (prior generation).</summary>
        public const string Gpt4o = "gpt-4o";

        /// <summary>GPT-4o Mini — fast multimodal (prior generation).</summary>
        public const string Gpt4oMini = "gpt-4o-mini";

        /// <summary>o3 — reasoning model.</summary>
        public const string O3 = "o3";

        /// <summary>o3-mini — compact reasoning.</summary>
        public const string O3Mini = "o3-mini";

        /// <summary>o4-mini — latest compact reasoning.</summary>
        public const string O4Mini = "o4-mini";
    }

    /// <summary>
    /// Anthropic Claude model identifiers. The SDK accepts these without
    /// the date suffix (e.g. <c>claude-sonnet-4</c> resolves to the latest
    /// point release of Sonnet 4).
    /// </summary>
    public static class Anthropic
    {
        /// <summary>Claude Opus 4 — most capable, complex reasoning.</summary>
        public const string Opus4 = "claude-opus-4";

        /// <summary>Claude Sonnet 4 — balanced performance and speed.</summary>
        public const string Sonnet4 = "claude-sonnet-4";

        /// <summary>Claude Sonnet 3.5 — prior generation balanced.</summary>
        public const string Sonnet35 = "claude-3-5-sonnet";

        /// <summary>Claude Haiku 3.5 — fastest, most compact.</summary>
        public const string Haiku35 = "claude-3-5-haiku";

        /// <summary>Claude Opus 4.8 — current generation, most capable, complex reasoning.</summary>
        public const string Opus48 = "claude-opus-4-8";

        /// <summary>Claude Sonnet 4.6 — current generation, balanced performance and speed.</summary>
        public const string Sonnet46 = "claude-sonnet-4-6";

        /// <summary>Claude Haiku 4.5 — current generation, fastest, most compact.</summary>
        public const string Haiku45 = "claude-haiku-4-5";
    }

    /// <summary>Google Gemini model identifiers. Already short wire names.</summary>
    public static class Google
    {
        /// <summary>Gemini 3.1 Pro — Agent Platform GA flagship, frontier reasoning.</summary>
        public const string Gemini31Pro = "gemini-3.1-pro";

        /// <summary>Gemini 3.1 Flash — Agent Platform GA fast model with reasoning.</summary>
        public const string Gemini31Flash = "gemini-3.1-flash";

        /// <summary>Gemini 3.1 Flash Image — multimodal variant with image generation.</summary>
        public const string Gemini31FlashImage = "gemini-3.1-flash-image";

        /// <summary>Gemma 4 — open-weight model available via Agent Platform / Model Garden.</summary>
        public const string Gemma4 = "gemma-4";

        /// <summary>Lyria 3 — Google DeepMind music / audio generation model.</summary>
        public const string Lyria3 = "lyria-3";

        /// <summary>Gemini 2.5 Pro — flagship, complex tasks.</summary>
        public const string Gemini25Pro = "gemini-2.5-pro";

        /// <summary>Gemini 2.5 Flash — fast, balanced.</summary>
        public const string Gemini25Flash = "gemini-2.5-flash";

        /// <summary>Gemini 2.0 Flash — prior generation fast.</summary>
        public const string Gemini20Flash = "gemini-2.0-flash";

        /// <summary>Gemini 2.0 Flash Lite — smallest, cheapest.</summary>
        public const string Gemini20FlashLite = "gemini-2.0-flash-lite";
    }
}
