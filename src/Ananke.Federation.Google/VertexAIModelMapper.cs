using Ananke.Abstractions.Agents;
using Ananke.Design;
using Ananke.Federation.Validation;

namespace Ananke.Federation.Google;

/// <summary>
/// Maps model definitions from other providers to Gemini Enterprise Agent Platform model identifiers.
/// Used during deployment to translate manifest model references.
/// </summary>
public sealed class VertexAIModelMapper : IModelMapper
{
    /// <inheritdoc />
    public string Platform => AgentPlatformConstants.Platform;

    // Provider/model → Gemini equivalent. Keys are "{provider}/{model}" lowercase.
    // Deprecated model constants are referenced on purpose: manifests written against a
    // deprecated-but-functional model must keep translating, and the Google passthrough entries
    // must echo the requested model verbatim, not upgrade it — ANNKE001 is expected here.
#pragma warning disable ANNKE001
    private static readonly Dictionary<string, string> Mappings = new(StringComparer.OrdinalIgnoreCase)
    {
        // OpenAI → Gemini (3.1-class defaults)
        [$"openai/{Models.OpenAI.Gpt41}"] = Models.Google.Gemini31Pro,
        [$"openai/{Models.OpenAI.Gpt41Mini}"] = Models.Google.Gemini31Flash,
        [$"openai/{Models.OpenAI.Gpt41Nano}"] = Models.Google.Gemini31FlashLite,
        [$"openai/{Models.OpenAI.Gpt4o}"] = Models.Google.Gemini31Pro,
        [$"openai/{Models.OpenAI.Gpt4oMini}"] = Models.Google.Gemini31Flash,
        [$"openai/{Models.OpenAI.O3}"] = Models.Google.Gemini31Pro,
        [$"openai/{Models.OpenAI.O3Mini}"] = Models.Google.Gemini31Flash,
        [$"openai/{Models.OpenAI.O4Mini}"] = Models.Google.Gemini31Flash,

        // Anthropic → Gemini (3.1-class defaults). Note: the current-gen Anthropic constants
        // (Opus48/Sonnet46/Haiku45/Sonnet5/Fable5/Opus41) have no entries here — this mapper was
        // never updated when those were added; tracked as a follow-up, not fixed as part of the
        // retired-model cleanup that removed the Opus4/Sonnet4/Sonnet35/Haiku35 entries below.

        // Google passthrough — all known model strings map to themselves
        [$"google/{Models.Google.Gemini31Pro}"] = Models.Google.Gemini31Pro,
        [$"google/{Models.Google.Gemini31Flash}"] = Models.Google.Gemini31Flash,
        [$"google/{Models.Google.Gemini31FlashImage}"] = Models.Google.Gemini31FlashImage,
        [$"google/{Models.Google.Gemini25Pro}"] = Models.Google.Gemini25Pro,
        [$"google/{Models.Google.Gemini25Flash}"] = Models.Google.Gemini25Flash,
    };
#pragma warning restore ANNKE001

    /// <inheritdoc />
    public string? Map(ModelDefinition model)
    {
        ArgumentNullException.ThrowIfNull(model);

        // Try direct provider/model lookup
        var key = $"{model.Provider}/{model.Model}";
        if (Mappings.TryGetValue(key, out var mapped))
            return mapped;

        // If provider is already google/vertex, pass through the model name
        if (string.Equals(model.Provider, "google", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(model.Provider, "vertex-ai", StringComparison.OrdinalIgnoreCase))
            return model.Model;

        return null;
    }
}
