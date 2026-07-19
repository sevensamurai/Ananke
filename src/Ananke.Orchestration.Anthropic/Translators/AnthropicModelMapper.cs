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
            // Anthropic → passthrough (already native). Note: claude-opus-4/claude-sonnet-4/
            // claude-3-5-sonnet/claude-3-5-haiku entries were removed — those models are retired.
            // Current-gen Anthropic passthrough keys were never added here; tracked as a
            // follow-up, not fixed as part of the retired-model cleanup.

            // OpenAI → nearest Claude equivalent
            ["openai/gpt-4.1"] = "claude-sonnet-5",
            ["openai/gpt-4.1-mini"] = "claude-haiku-4-5",
            ["openai/gpt-4.1-nano"] = "claude-haiku-4-5",
            ["openai/gpt-4o"] = "claude-sonnet-5",
            ["openai/gpt-4o-mini"] = "claude-haiku-4-5",
            ["openai/o3"] = "claude-sonnet-5",
            ["openai/o3-mini"] = "claude-haiku-4-5",
            ["openai/o4-mini"] = "claude-haiku-4-5",

            // Google → nearest Claude equivalent
            ["google/gemini-2.5-pro"] = "claude-sonnet-5",
            ["google/gemini-2.5-flash"] = "claude-haiku-4-5",
        };

    private static readonly Dictionary<string, ModelCapabilityFlags> Capabilities =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["claude-sonnet-5"] = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.Streaming,
            ["claude-haiku-4-5"] = ModelCapabilityFlags.ToolCalling | ModelCapabilityFlags.StructuredOutput | ModelCapabilityFlags.Vision | ModelCapabilityFlags.Streaming,
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
