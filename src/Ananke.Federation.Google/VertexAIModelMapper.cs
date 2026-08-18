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

        // Anthropic → Gemini: no entries. The current-gen Anthropic constants were never added
        // here, so a manifest naming one falls through to Map()'s null. Open follow-up, same gap
        // as AzureModelMapper.

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
