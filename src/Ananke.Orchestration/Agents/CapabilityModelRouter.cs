namespace Ananke.Orchestration.Agents;

/// <summary>
/// Determines which candidate model is preferred when multiple
/// <see cref="ModelProfile"/> instances satisfy a task's requirements.
/// </summary>
public enum RoutingStrategy
{
    /// <summary>Cheapest model that meets requirements. Ties broken by speed (fastest wins).</summary>
    CheapestFit,

    /// <summary>Fastest model that meets requirements. Ties broken by cost (cheapest wins).</summary>
    FastestFit,

    /// <summary>Highest intelligence that meets requirements. Ties broken by cost (cheapest wins).</summary>
    BestFit
}

/// <summary>
/// An <see cref="IModelRouter"/> that selects the optimal <see cref="IAgentModel"/> based on
/// <see cref="ModelProfile"/> metadata and <see cref="TaskRequirements"/> inferred from each request.
/// <para>
/// Configure profiles once, and the router automatically picks the cheapest (or fastest, or best)
/// model that satisfies every request — no manual predicates required.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var router = new CapabilityModelRouter(RoutingStrategy.CheapestFit)
///     .AddModel(new ModelProfile
///     {
///         Name = "gpt-4o-mini",
///         Model = miniModel,
///         Capabilities = ModelCapability.TextGeneration | ModelCapability.ToolCalling
///                      | ModelCapability.StructuredOutput,
///         IntelligenceTier = 2, CostPer1KTokens = 0.15m,
///         MaxContextTokens = 128_000, SpeedTier = 4
///     })
///     .AddModel(new ModelProfile
///     {
///         Name = "gpt-4o",
///         Model = fullModel,
///         Capabilities = ModelCapability.TextGeneration | ModelCapability.CodeGeneration
///                      | ModelCapability.Reasoning | ModelCapability.ToolCalling
///                      | ModelCapability.StructuredOutput | ModelCapability.Vision
///                      | ModelCapability.LargeContext,
///         IntelligenceTier = 4, CostPer1KTokens = 2.50m,
///         MaxContextTokens = 128_000, SpeedTier = 3
///     });
///
/// // Simple tasks → gpt-4o-mini, complex tasks requiring reasoning → gpt-4o
/// var agent = new AgentJob&lt;MyState, MyResponse&gt;.Builder("analyze", router)
///     .WithPrompt(s =&gt; s.Data)
///     .MapResult((s, r) =&gt; s with { Result = r })
///     .Build();
/// </code>
/// </example>
public sealed class CapabilityModelRouter : IModelRouter
{
    private readonly List<ModelProfile> _profiles = [];
    private readonly RoutingStrategy _strategy;
    private ModelProfile? _fallback;

    public CapabilityModelRouter(RoutingStrategy strategy = RoutingStrategy.CheapestFit)
    {
        _strategy = strategy;
    }

    /// <summary>Registers a model profile as a routing candidate.</summary>
    public CapabilityModelRouter AddModel(ModelProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _profiles.Add(profile);
        return this;
    }

    /// <summary>
    /// Sets a fallback profile used when no candidate satisfies the inferred requirements.
    /// Typically the highest-capability model that can handle any task.
    /// </summary>
    public CapabilityModelRouter WithFallback(ModelProfile fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        _fallback = fallback;
        return this;
    }

    /// <inheritdoc />
    public IAgentModel Select(AgentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requirements = TaskRequirements.InferFrom(request);
        var candidates = _profiles.Where(p => p.Satisfies(requirements)).ToList();

        if (candidates.Count == 0)
        {
            return _fallback?.Model
                ?? throw new InvalidOperationException(
                    $"No model satisfies the inferred requirements ({requirements.RequiredCapabilities}, " +
                    $"tier ≥ {requirements.MinIntelligenceTier}) and no fallback is configured. " +
                    $"Call WithFallback() or add a model with broader capabilities.");
        }

        var selected = _strategy switch
        {
            RoutingStrategy.CheapestFit => candidates
                .OrderBy(p => p.CostPer1KTokens)
                .ThenByDescending(p => p.SpeedTier)
                .First(),
            RoutingStrategy.FastestFit => candidates
                .OrderByDescending(p => p.SpeedTier)
                .ThenBy(p => p.CostPer1KTokens)
                .First(),
            RoutingStrategy.BestFit => candidates
                .OrderByDescending(p => p.IntelligenceTier)
                .ThenBy(p => p.CostPer1KTokens)
                .First(),
            _ => candidates[0]
        };

        return selected.Model;
    }

    /// <summary>Wraps this router as an <see cref="IAgentModel"/> for use in <c>AgentJob</c>.</summary>
    public IAgentModel ToAgentModel() => new RoutedAgentModel(this);
}
