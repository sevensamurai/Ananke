namespace Ananke.Design;

/// <summary>
/// Validates model identifiers against known provider catalogs. Detects ambiguous
/// family names (e.g. <c>sonnet</c> without a version) and unknown models.
/// </summary>
/// <remarks>
/// <para>
/// The catalog does <b>not</b> rewrite model strings — it only validates.
/// The string in the manifest is the string that reaches the SDK.
/// Unknown models produce a warning (not an error) to stay future-proof.
/// </para>
/// </remarks>
public static class ModelCatalog
{
    /// <summary>
    /// Result of validating a model identifier.
    /// </summary>
    public sealed record ValidationResult
    {
        /// <summary>Whether the model identifier is valid.</summary>
        public required bool IsValid { get; init; }

        /// <summary>Warning or error message, if any.</summary>
        public string? Message { get; init; }

        /// <summary>Suggested alternatives when the input is ambiguous or unknown.</summary>
        public IReadOnlyList<string> Suggestions { get; init; } = [];
    }

    // Provider → { family name → list of known versioned models }
    private static readonly Dictionary<string, Dictionary<string, List<string>>> Families = new(StringComparer.OrdinalIgnoreCase)
    {
        ["openai"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt-4.1"] = [Models.OpenAI.Gpt41, Models.OpenAI.Gpt41Mini, Models.OpenAI.Gpt41Nano],
            ["gpt-4o"] = [Models.OpenAI.Gpt4o, Models.OpenAI.Gpt4oMini],
            ["o3"] = [Models.OpenAI.O3, Models.OpenAI.O3Mini],
            ["o4"] = [Models.OpenAI.O4Mini],
        },
        ["anthropic"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["opus"] = [Models.Anthropic.Opus4],
            ["sonnet"] = [Models.Anthropic.Sonnet4, Models.Anthropic.Sonnet35],
            ["haiku"] = [Models.Anthropic.Haiku35],
        },
        ["google"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["gemini"] = [Models.Google.Gemini31Pro, Models.Google.Gemini31Flash, Models.Google.Gemini25Pro, Models.Google.Gemini25Flash, Models.Google.Gemini20Flash, Models.Google.Gemini20FlashLite],
            ["pro"]    = [Models.Google.Gemini31Pro, Models.Google.Gemini25Pro],
            ["flash"]  = [Models.Google.Gemini31Flash, Models.Google.Gemini25Flash, Models.Google.Gemini20Flash, Models.Google.Gemini20FlashLite],
        },
    };

    // Provider → set of all known valid model strings
    private static readonly Dictionary<string, HashSet<string>> KnownModels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["openai"] = new(StringComparer.OrdinalIgnoreCase)
        {
            Models.OpenAI.Gpt41, Models.OpenAI.Gpt41Mini, Models.OpenAI.Gpt41Nano,
            Models.OpenAI.Gpt4o, Models.OpenAI.Gpt4oMini,
            Models.OpenAI.O3, Models.OpenAI.O3Mini, Models.OpenAI.O4Mini,
        },
        ["anthropic"] = new(StringComparer.OrdinalIgnoreCase)
        {
            Models.Anthropic.Opus4, Models.Anthropic.Sonnet4,
            Models.Anthropic.Sonnet35, Models.Anthropic.Haiku35,
        },
        ["google"] = new(StringComparer.OrdinalIgnoreCase)
        {
            Models.Google.Gemini31Pro, Models.Google.Gemini31Flash, Models.Google.Gemini31FlashImage,
            Models.Google.Gemma4, Models.Google.Lyria3,
            Models.Google.Gemini25Pro, Models.Google.Gemini25Flash,
            Models.Google.Gemini20Flash, Models.Google.Gemini20FlashLite,
        },
    };

    /// <summary>
    /// Validates a model identifier for a given provider.
    /// </summary>
    /// <param name="provider">Provider name (e.g. <c>"openai"</c>, <c>"anthropic"</c>).</param>
    /// <param name="model">Model identifier to validate.</param>
    /// <returns>Validation result with suggestions when ambiguous.</returns>
    public static ValidationResult Validate(string provider, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        // Unknown provider — pass through with info
        if (!Families.TryGetValue(provider, out var families))
        {
            return new ValidationResult
            {
                IsValid = true,
                Message = $"Unknown provider '{provider}' — model '{model}' will be passed through to the SDK as-is."
            };
        }

        // Check if it's a known valid model first (before family check)
        if (KnownModels.TryGetValue(provider, out var known) && known.Contains(model))
        {
            return new ValidationResult { IsValid = true };
        }

        // Check if model is a bare family name (ambiguous)
        if (families.TryGetValue(model, out var versions) && versions.Count > 1)
        {
            return new ValidationResult
            {
                IsValid = false,
                Message = $"'{model}' is ambiguous for provider '{provider}'. Specify a version.",
                Suggestions = versions
            };
        }

        // Not in our catalog — might be a pinned version (claude-sonnet-4-20250514),
        // a new model we don't know about, or a typo. Warn but allow.
        return new ValidationResult
        {
            IsValid = true,
            Message = $"Model '{model}' is not in the known catalog for '{provider}'. It will be passed through to the SDK — verify the name is correct.",
            Suggestions = known?.Order().ToList() ?? []
        };
    }

    /// <summary>
    /// Gets all known model identifiers for a provider.
    /// </summary>
    public static IReadOnlyList<string> GetModels(string provider)
    {
        if (KnownModels.TryGetValue(provider, out var known))
            return known.Order().ToList();
        return [];
    }

    /// <summary>
    /// Gets all known providers.
    /// </summary>
    public static IReadOnlyList<string> GetProviders() =>
        [.. KnownModels.Keys.Order()];
}
