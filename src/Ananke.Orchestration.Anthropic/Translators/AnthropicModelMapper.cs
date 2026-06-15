using Ananke.Abstractions.Providers;

namespace Ananke.Orchestration.Anthropic.Translators;

/// <summary>
/// Maps Ananke logical model identifiers to Anthropic Claude model names.
/// </summary>
public sealed class AnthropicModelMapper : IModelMapper
{
    /// <inheritdoc />
    public string Platform => "anthropic";

    private static readonly Dictionary<string, string> Mappings =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Anthropic → passthrough (already native)
            ["anthropic/claude-opus-4"]      = "claude-opus-4",
            ["anthropic/claude-sonnet-4"]    = "claude-sonnet-4",
            ["anthropic/claude-3-5-sonnet"]  = "claude-3-5-sonnet",
            ["anthropic/claude-3-5-haiku"]   = "claude-3-5-haiku",

            // OpenAI → nearest Claude equivalent
            ["openai/gpt-4.1"]      = "claude-sonnet-4",
            ["openai/gpt-4.1-mini"] = "claude-3-5-haiku",
            ["openai/gpt-4.1-nano"] = "claude-3-5-haiku",
            ["openai/gpt-4o"]       = "claude-sonnet-4",
            ["openai/gpt-4o-mini"]  = "claude-3-5-haiku",
            ["openai/o3"]           = "claude-sonnet-4",
            ["openai/o3-mini"]      = "claude-3-5-haiku",
            ["openai/o4-mini"]      = "claude-3-5-haiku",

            // Google → nearest Claude equivalent
            ["google/gemini-2.5-pro"]       = "claude-sonnet-4",
            ["google/gemini-2.5-flash"]     = "claude-3-5-haiku",
            ["google/gemini-2.0-flash"]     = "claude-3-5-haiku",
            ["google/gemini-2.0-flash-lite"]= "claude-3-5-haiku",
        };

    private static readonly Dictionary<string, ModelCapabilityFlags> Capabilities =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["claude-opus-4"]    = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.Streaming,
            ["claude-sonnet-4"]  = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.Streaming,
            ["claude-3-5-sonnet"]= ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.Streaming,
            ["claude-3-5-haiku"] = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.Streaming,
        };

    /// <inheritdoc />
    public string? MapModelId(string logicalModelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logicalModelId);

        if (Mappings.TryGetValue(logicalModelId, out var mapped))
            return mapped;

        // Bare model id or anthropic/claude provider — pass through
        if (!logicalModelId.Contains('/') ||
            logicalModelId.StartsWith("anthropic/", StringComparison.OrdinalIgnoreCase) ||
            logicalModelId.StartsWith("claude/", StringComparison.OrdinalIgnoreCase))
        {
            var bare = logicalModelId.Contains('/')
                ? logicalModelId[(logicalModelId.IndexOf('/') + 1)..]
                : logicalModelId;
            return bare;
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
