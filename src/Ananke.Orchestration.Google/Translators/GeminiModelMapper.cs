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
            ["openai/gpt-4.1"]      = "gemini-2.5-pro",
            ["openai/gpt-4.1-mini"] = "gemini-2.5-flash",
            ["openai/gpt-4.1-nano"] = "gemini-2.0-flash-lite",
            ["openai/gpt-4o"]       = "gemini-2.5-pro",
            ["openai/gpt-4o-mini"]  = "gemini-2.5-flash",
            ["openai/o3"]           = "gemini-2.5-pro",
            ["openai/o3-mini"]      = "gemini-2.5-flash",
            ["openai/o4-mini"]      = "gemini-2.5-flash",

            // Anthropic → Gemini
            ["anthropic/claude-opus-4"]     = "gemini-2.5-pro",
            ["anthropic/claude-sonnet-4"]   = "gemini-2.5-pro",
            ["anthropic/claude-3-5-sonnet"] = "gemini-2.5-pro",
            ["anthropic/claude-3-5-haiku"]  = "gemini-2.5-flash",

            // Google → passthrough
            ["google/gemini-2.5-pro"]        = "gemini-2.5-pro",
            ["google/gemini-2.5-flash"]      = "gemini-2.5-flash",
            ["google/gemini-2.0-flash"]      = "gemini-2.0-flash",
            ["google/gemini-2.0-flash-lite"] = "gemini-2.0-flash-lite",
        };

    private static readonly Dictionary<string, ModelCapabilityFlags> Capabilities =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["gemini-2.5-pro"]       = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.AudioInput | ModelCapabilityFlags.Streaming,
            ["gemini-2.5-flash"]     = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.AudioInput | ModelCapabilityFlags.Streaming,
            ["gemini-2.0-flash"]     = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.AudioInput | ModelCapabilityFlags.Streaming,
            ["gemini-2.0-flash-lite"]= ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.Streaming,
        };

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
