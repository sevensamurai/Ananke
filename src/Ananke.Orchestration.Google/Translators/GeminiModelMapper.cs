using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Providers;

namespace Ananke.Orchestration.Google.Translators;

/// <summary>
/// Maps Ananke logical model identifiers to Google Gemini native model names.
/// </summary>
public sealed class GeminiModelMapper : IModelMapper
{
    /// <inheritdoc />
    public string Platform => "google";

    private static readonly Dictionary<string, string> Mappings =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // OpenAI → Gemini
            ["openai/gpt-4.1"] = Models.Google.Gemini31Pro,
            ["openai/gpt-4.1-mini"] = Models.Google.Gemini35Flash,
            ["openai/gpt-4.1-nano"] = "gemini-3.1-flash-lite",
            ["openai/gpt-4o"] = Models.Google.Gemini31Pro,
            ["openai/gpt-4o-mini"] = Models.Google.Gemini35Flash,
            ["openai/o3"] = Models.Google.Gemini31Pro,
            ["openai/o3-mini"] = Models.Google.Gemini35Flash,
            ["openai/o4-mini"] = Models.Google.Gemini35Flash,

            // Anthropic → Gemini. Note: claude-opus-4/claude-sonnet-4/claude-3-5-sonnet/
            // claude-3-5-haiku source-model entries were removed here — those Anthropic models
            // are retired, so no manifest should be requesting them. Current-gen Anthropic source
            // keys (claude-opus-4-8, claude-sonnet-4-6, etc.) were never added to this mapper;
            // tracked as a follow-up, not fixed as part of the retired-model cleanup.

            // Google → passthrough (identity — must echo the input verbatim, not upgrade it;
            // do not apply the ANNKE002 code fix here — it broke this exact identity mapping once)
#pragma warning disable ANNKE002
            ["google/gemini-2.5-pro"] = "gemini-2.5-pro",
            ["google/gemini-2.5-flash"] = "gemini-2.5-flash",
#pragma warning restore ANNKE002
        };

    // Deprecated ids stay as Capabilities keys on purpose: passthrough outputs above still
    // resolve to them, and callers of GetCapabilities must keep getting real flags for a
    // deprecated-but-functional model.
#pragma warning disable ANNKE002
    private static readonly Dictionary<string, ModelCapabilityFlags> Capabilities =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["gemini-2.5-pro"] = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.AudioInput | ModelCapabilityFlags.Streaming,
            ["gemini-2.5-flash"] = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.AudioInput | ModelCapabilityFlags.Streaming,
            ["gemini-3.1-flash-lite"] = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.Streaming,

            // Current-gen entries: the OpenAI → Gemini mappings above resolve to these ids, and
            // GetCapabilities must recognize every id MapModelId can return (see IModelMapper).
            [Models.Google.Gemini31Pro] = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.AudioInput | ModelCapabilityFlags.Streaming,
            [Models.Google.Gemini35Flash] = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.AudioInput | ModelCapabilityFlags.Streaming,
        };
#pragma warning restore ANNKE002

    /// <inheritdoc />
    public string? MapModelId(string logicalModelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalModelId);

        if (Mappings.TryGetValue(logicalModelId, out var mapped))
            return mapped;

        // Bare id or google/vertex prefix — pass through
        if (!logicalModelId.Contains('/') ||
            logicalModelId.StartsWith("google/", StringComparison.OrdinalIgnoreCase) ||
            logicalModelId.StartsWith("vertex-ai/", StringComparison.OrdinalIgnoreCase))
        {
            return logicalModelId.Contains('/')
                ? logicalModelId[(logicalModelId.IndexOf('/') + 1)..]
                : logicalModelId;
        }

        return null;
    }

    /// <inheritdoc />
    public ModelCapabilityFlags? GetCapabilities(string nativeModelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeModelId);
        return Capabilities.TryGetValue(nativeModelId, out var flags) ? flags : null;
    }
}
