using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Providers;

namespace Ananke.Orchestration.OpenAI.Translators;

/// <summary>
/// Maps Ananke logical model identifiers to OpenAI model names.
/// </summary>
public sealed class OpenAIModelMapper : IModelMapper
{
    /// <inheritdoc />
    public string Platform => "openai";

    private static readonly Dictionary<string, string> Mappings =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // OpenAI → passthrough (identity — must echo the input verbatim, not upgrade it;
            // do not apply the ANNKE002 code fix here — it broke this exact identity mapping once)
#pragma warning disable ANNKE002
            ["openai/gpt-4.1"] = "gpt-4.1",
            ["openai/gpt-4.1-mini"] = "gpt-4.1-mini",
            ["openai/gpt-4.1-nano"] = "gpt-4.1-nano",
            ["openai/gpt-4o"] = "gpt-4o",
            ["openai/gpt-4o-mini"] = "gpt-4o-mini",
            ["openai/o3"] = "o3",
            ["openai/o3-mini"] = "o3-mini",
            ["openai/o4-mini"] = "o4-mini",
#pragma warning restore ANNKE002

            // Google → nearest OpenAI equivalent
            ["google/gemini-2.5-pro"] = Models.OpenAI.Gpt56Sol,
            ["google/gemini-2.5-flash"] = Models.OpenAI.Gpt56Terra,

            // Anthropic → nearest OpenAI equivalent. Note: claude-opus-4/claude-sonnet-4/
            // claude-3-5-sonnet/claude-3-5-haiku source-model entries were removed here — those
            // Anthropic models are retired. Current-gen Anthropic source keys were never added to
            // this mapper; tracked as a follow-up, not fixed as part of the retired-model cleanup.
        };

    // Deprecated ids stay as Capabilities keys on purpose: passthrough outputs above still
    // resolve to them, and callers of GetCapabilities must keep getting real flags for a
    // deprecated-but-functional model.
#pragma warning disable ANNKE002
    private static readonly Dictionary<string, ModelCapabilityFlags> Capabilities =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt-4.1"] = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.Streaming,
            ["gpt-4.1-mini"] = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.Streaming,
            ["gpt-4.1-nano"] = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Streaming,
            ["gpt-4o"] = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.AudioInput | ModelCapabilityFlags.Streaming,
            ["gpt-4o-mini"] = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.Streaming,
            ["o3"] = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.Streaming,
            ["o3-mini"] = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Streaming,
            ["o4-mini"] = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.Streaming,

            // Legacy entries, kept so a direct passthrough request (e.g. "openai/gpt-5.5") still
            // resolves capabilities even though nothing in the Mappings table above targets them
            // anymore.
            [Models.OpenAI.Gpt55] = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.Streaming,
            [Models.OpenAI.Gpt54Mini] = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.Streaming,

            // Current-gen entries: the Google → OpenAI mappings above resolve to these ids, and
            // GetCapabilities must recognize every id MapModelId can return (see IModelMapper).
            [Models.OpenAI.Gpt56Sol] = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.Streaming,
            [Models.OpenAI.Gpt56Terra] = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.Streaming,
        };
#pragma warning restore ANNKE002

    /// <inheritdoc />
    public string? MapModelId(string logicalModelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalModelId);

        if (Mappings.TryGetValue(logicalModelId, out var mapped))
            return mapped;

        // Bare model id or openai/ prefix — pass through
        if (!logicalModelId.Contains('/') ||
            logicalModelId.StartsWith("openai/", StringComparison.OrdinalIgnoreCase) ||
            logicalModelId.StartsWith("azure/", StringComparison.OrdinalIgnoreCase))
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
