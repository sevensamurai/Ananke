using Ananke.Orchestration.Translators;

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
            // OpenAI → passthrough
            ["openai/gpt-4.1"]      = "gpt-4.1",
            ["openai/gpt-4.1-mini"] = "gpt-4.1-mini",
            ["openai/gpt-4.1-nano"] = "gpt-4.1-nano",
            ["openai/gpt-4o"]       = "gpt-4o",
            ["openai/gpt-4o-mini"]  = "gpt-4o-mini",
            ["openai/o3"]           = "o3",
            ["openai/o3-mini"]      = "o3-mini",
            ["openai/o4-mini"]      = "o4-mini",

            // Google → nearest OpenAI equivalent
            ["google/gemini-2.5-pro"]       = "gpt-4.1",
            ["google/gemini-2.5-flash"]     = "gpt-4.1-mini",
            ["google/gemini-2.0-flash"]     = "gpt-4.1-mini",
            ["google/gemini-2.0-flash-lite"]= "gpt-4.1-nano",

            // Anthropic → nearest OpenAI equivalent
            ["anthropic/claude-opus-4"]     = "gpt-4.1",
            ["anthropic/claude-sonnet-4"]   = "gpt-4.1",
            ["anthropic/claude-3-5-sonnet"] = "gpt-4.1",
            ["anthropic/claude-3-5-haiku"]  = "gpt-4.1-mini",
        };

    private static readonly Dictionary<string, ModelCapabilityFlags> Capabilities =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt-4.1"]      = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.Streaming,
            ["gpt-4.1-mini"] = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.Streaming,
            ["gpt-4.1-nano"] = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Streaming,
            ["gpt-4o"]       = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.AudioInput | ModelCapabilityFlags.Streaming,
            ["gpt-4o-mini"]  = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.Streaming,
            ["o3"]           = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.Streaming,
            ["o3-mini"]      = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Streaming,
            ["o4-mini"]      = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.Streaming,
        };

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
