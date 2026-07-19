using Ananke.Abstractions.Agents;
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
    // Deprecated source-model constants are referenced on purpose: manifests written against a
    // deprecated-but-functional model must keep translating — ANNKE001 is expected here.
#pragma warning disable ANNKE001
    private static readonly Dictionary<string, string> Mappings = new(StringComparer.OrdinalIgnoreCase)
    {
        // Anthropic models — explicit entries are unnecessary (Map() already passes anthropic
        // models through generically below); the retired Opus4/Sonnet4/Sonnet35/Haiku35 passthrough
        // entries that used to live here were removed along with the constants.

        // OpenAI → nearest Claude equivalent
        [$"openai/{Models.OpenAI.Gpt41}"] = Models.Anthropic.Sonnet5,
        [$"openai/{Models.OpenAI.Gpt41Mini}"] = Models.Anthropic.Haiku45,
        [$"openai/{Models.OpenAI.Gpt41Nano}"] = Models.Anthropic.Haiku45,
        [$"openai/{Models.OpenAI.Gpt4o}"] = Models.Anthropic.Sonnet5,
        [$"openai/{Models.OpenAI.Gpt4oMini}"] = Models.Anthropic.Haiku45,
        [$"openai/{Models.OpenAI.O3}"] = Models.Anthropic.Sonnet5,
        [$"openai/{Models.OpenAI.O3Mini}"] = Models.Anthropic.Haiku45,
        [$"openai/{Models.OpenAI.O4Mini}"] = Models.Anthropic.Haiku45,

        // Google → nearest Claude equivalent
        [$"google/{Models.Google.Gemini25Pro}"] = Models.Anthropic.Sonnet5,
        [$"google/{Models.Google.Gemini25Flash}"] = Models.Anthropic.Haiku45,
    };
#pragma warning restore ANNKE001

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
