using Ananke.Abstractions.Agents;
using Ananke.Design;
using Ananke.Federation.Validation;

namespace Ananke.Federation.Azure;

/// <summary>
/// Maps model definitions to Azure AI Agent Service model identifiers.
/// OpenAI models pass through directly; other providers are mapped to their
/// Azure-hosted equivalents when available.
/// </summary>
public sealed class AzureModelMapper : IModelMapper
{
    /// <inheritdoc />
    public string Platform => "azure-ai";

    // Provider/model → Azure model deployment name. Keys are "{provider}/{model}" lowercase.
    // Deprecated model constants are referenced on purpose: manifests written against a
    // deprecated-but-functional model must keep translating, and the OpenAI passthrough entries
    // must echo the requested model verbatim, not upgrade it — ANNKE001 is expected here.
#pragma warning disable ANNKE001
    private static readonly Dictionary<string, string> Mappings = new(StringComparer.OrdinalIgnoreCase)
    {
        // OpenAI models — pass through (Azure uses the same model names)
        [$"openai/{Models.OpenAI.Gpt41}"] = Models.OpenAI.Gpt41,
        [$"openai/{Models.OpenAI.Gpt41Mini}"] = Models.OpenAI.Gpt41Mini,
        [$"openai/{Models.OpenAI.Gpt41Nano}"] = Models.OpenAI.Gpt41Nano,
        [$"openai/{Models.OpenAI.Gpt4o}"] = Models.OpenAI.Gpt4o,
        [$"openai/{Models.OpenAI.Gpt4oMini}"] = Models.OpenAI.Gpt4oMini,
        [$"openai/{Models.OpenAI.O3}"] = Models.OpenAI.O3,
        [$"openai/{Models.OpenAI.O3Mini}"] = Models.OpenAI.O3Mini,
        [$"openai/{Models.OpenAI.O4Mini}"] = Models.OpenAI.O4Mini,

        // Google → nearest Azure-hosted equivalent
        [$"google/{Models.Google.Gemini25Pro}"] = Models.OpenAI.Gpt41,
        [$"google/{Models.Google.Gemini25Flash}"] = Models.OpenAI.Gpt41Mini,

        // Anthropic → nearest Azure-hosted equivalent: no entries. The current-gen Anthropic
        // constants were never added here, so a manifest naming one falls through to Map()'s null.
        // Open follow-up, same gap as VertexAIModelMapper.
    };
#pragma warning restore ANNKE001

    /// <inheritdoc />
    public string? Map(ModelDefinition model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var key = $"{model.Provider}/{model.Model}";
        if (Mappings.TryGetValue(key, out var mapped))
            return mapped;

        // If provider is already openai or azure, pass through the model name
        if (string.Equals(model.Provider, "openai", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(model.Provider, "azure", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(model.Provider, "azure-ai", StringComparison.OrdinalIgnoreCase))
            return model.Model;

        return null;
    }
}
