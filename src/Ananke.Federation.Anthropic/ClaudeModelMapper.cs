using Ananke.Design;
using Ananke.Federation.Validation;

namespace Ananke.Federation.Anthropic;

/// <summary>
/// Maps model definitions to Claude model identifiers.
/// Anthropic models pass through directly; other providers are mapped to their
/// nearest Claude equivalent when available.
/// </summary>
public sealed class ClaudeModelMapper : IModelMapper
{
    /// <inheritdoc />
    public string Platform => "claude";

    // Provider/model → Claude model identifier. Keys are "{provider}/{model}" lowercase.
    private static readonly Dictionary<string, string> Mappings = new(StringComparer.OrdinalIgnoreCase)
    {
        // Anthropic models — pass through
        [$"anthropic/{Models.Anthropic.Opus4}"] = Models.Anthropic.Opus4,
        [$"anthropic/{Models.Anthropic.Sonnet4}"] = Models.Anthropic.Sonnet4,
        [$"anthropic/{Models.Anthropic.Sonnet35}"] = Models.Anthropic.Sonnet35,
        [$"anthropic/{Models.Anthropic.Haiku35}"] = Models.Anthropic.Haiku35,

        // OpenAI → nearest Claude equivalent
        [$"openai/{Models.OpenAI.Gpt41}"] = Models.Anthropic.Sonnet4,
        [$"openai/{Models.OpenAI.Gpt41Mini}"] = Models.Anthropic.Haiku35,
        [$"openai/{Models.OpenAI.Gpt41Nano}"] = Models.Anthropic.Haiku35,
        [$"openai/{Models.OpenAI.Gpt4o}"] = Models.Anthropic.Sonnet4,
        [$"openai/{Models.OpenAI.Gpt4oMini}"] = Models.Anthropic.Haiku35,
        [$"openai/{Models.OpenAI.O3}"] = Models.Anthropic.Sonnet4,
        [$"openai/{Models.OpenAI.O3Mini}"] = Models.Anthropic.Haiku35,
        [$"openai/{Models.OpenAI.O4Mini}"] = Models.Anthropic.Haiku35,

        // Google → nearest Claude equivalent
        [$"google/{Models.Google.Gemini25Pro}"] = Models.Anthropic.Sonnet4,
        [$"google/{Models.Google.Gemini25Flash}"] = Models.Anthropic.Haiku35,
        [$"google/{Models.Google.Gemini20Flash}"] = Models.Anthropic.Haiku35,
        [$"google/{Models.Google.Gemini20FlashLite}"] = Models.Anthropic.Haiku35,
    };

    /// <inheritdoc />
    public string? Map(ModelDefinition model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var key = $"{model.Provider}/{model.Model}";
        if (Mappings.TryGetValue(key, out var mapped))
            return mapped;

        // If provider is already anthropic or claude, pass through the model name
        if (string.Equals(model.Provider, "anthropic", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(model.Provider, "claude", StringComparison.OrdinalIgnoreCase))
            return model.Model;

        return null;
    }
}
