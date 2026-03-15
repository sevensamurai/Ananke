namespace Ananke.Orchestration.Agents;

/// <summary>
/// Describes the minimum model capabilities needed for a task.
/// Create explicitly or let <see cref="InferFrom"/> derive requirements
/// from an <see cref="AgentRequest"/>.
/// </summary>
/// <remarks>
/// Explicit overrides are read from <see cref="AgentRequest.Metadata"/>:
/// <list type="bullet">
///   <item><c>required_capabilities</c> — comma-separated <see cref="ModelCapability"/> flags</item>
///   <item><c>min_intelligence</c> — integer tier (1–5)</item>
///   <item><c>min_context_tokens</c> — integer token count</item>
/// </list>
/// Use the <see cref="AgentRequestExtensions.WithRequiredCapabilities"/>,
/// <see cref="AgentRequestExtensions.WithMinIntelligence"/>, and
/// <see cref="AgentRequestExtensions.WithMinContextTokens"/> helpers to set these values.
/// </remarks>
public sealed record TaskRequirements
{
    /// <summary>Capability flags the model must support.</summary>
    public ModelCapability RequiredCapabilities { get; init; }

    /// <summary>Minimum intelligence tier (1–5). Defaults to 1.</summary>
    public int MinIntelligenceTier { get; init; } = 1;

    /// <summary>Minimum context window in tokens. Zero means no constraint.</summary>
    public int MinContextTokens { get; init; }

    /// <summary>
    /// Infers requirements from the structure and metadata of an <see cref="AgentRequest"/>.
    /// <para>
    /// Structural inference:
    /// <list type="bullet">
    ///   <item><see cref="AgentRequestExtensions.HasTools"/> → <see cref="ModelCapability.ToolCalling"/></item>
    ///   <item><see cref="AgentRequestExtensions.HasStructuredOutput"/> → <see cref="ModelCapability.StructuredOutput"/></item>
    ///   <item>Estimated content &gt; 64 K chars (~16 K tokens) → <see cref="ModelCapability.LargeContext"/></item>
    /// </list>
    /// Metadata keys override or augment the inferred values.
    /// </para>
    /// </summary>
    public static TaskRequirements InferFrom(AgentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caps = ModelCapability.TextGeneration;

        if (request.HasTools())
            caps |= ModelCapability.ToolCalling;

        if (request.HasStructuredOutput())
            caps |= ModelCapability.StructuredOutput;

        // Detect multimodal content parts
        foreach (var message in request.Messages)
        {
            if (message.Parts is not { Count: > 0 })
                continue;

            foreach (var part in message.Parts)
            {
                switch (part)
                {
                    case AudioPart:
                        caps |= ModelCapability.AudioInput;
                        break;
                    case ImagePart:
                        caps |= ModelCapability.Vision;
                        break;
                }
            }
        }

        var minTier = 1;
        var minContext = 0;

        if (request.Metadata is not null)
        {
            if (request.Metadata.TryGetValue("required_capabilities", out var capsStr) &&
                Enum.TryParse<ModelCapability>(capsStr, true, out var explicitCaps))
                caps |= explicitCaps;

            if (request.Metadata.TryGetValue("min_intelligence", out var tierStr) &&
                int.TryParse(tierStr, out var tier))
                minTier = Math.Max(minTier, tier);

            if (request.Metadata.TryGetValue("min_context_tokens", out var ctxStr) &&
                int.TryParse(ctxStr, out var ctx))
                minContext = ctx;
        }

        // Flag large-context when estimated content exceeds ~16 K tokens (64 K chars)
        if (minContext <= 0 && request.EstimatedContentLength() > 64_000)
            caps |= ModelCapability.LargeContext;

        return new TaskRequirements
        {
            RequiredCapabilities = caps,
            MinIntelligenceTier = minTier,
            MinContextTokens = minContext
        };
    }
}
