using System.Collections.Concurrent;
using Ananke.Abstractions.Agents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ananke.Orchestration.Agents.Routing;

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
    BestFit,

    /// <summary>
    /// Composite score: <c>−cost × W_cost + speed × W_speed + intelligence × W_intelligence</c>.
    /// Configure weights via <see cref="CapabilityModelRouter(RoutingWeights, ILogger)"/>.
    /// </summary>
    Weighted,

    /// <summary>
    /// Custom scoring delegate. Configure via
    /// <see cref="CapabilityModelRouter(Func{ModelProfile, decimal}, ILogger)"/>.
    /// </summary>
    Custom
}

/// <summary>
/// Weights for the <see cref="RoutingStrategy.Weighted"/> strategy.
/// Each weight controls how much that dimension contributes to the
/// composite score. Cost is inverted (lower cost → higher score).
/// </summary>
/// <remarks>
/// The scoring formula is:
/// <c>score = −CostPer1KTokens × CostWeight + SpeedTier × SpeedWeight + IntelligenceTier × IntelligenceWeight</c>.
/// All weights default to <c>1.0</c> (equal importance).
/// </remarks>
/// <example>
/// <code>
/// // Prefer speed over cost, ignore intelligence:
/// var weights = new RoutingWeights { SpeedWeight = 2.0m, CostWeight = 0.5m, IntelligenceWeight = 0m };
/// var router = new CapabilityModelRouter(weights);
/// </code>
/// </example>
public sealed record RoutingWeights
{
    /// <summary>
    /// Weight for cost (inverted — lower cost scores higher).
    /// Set to <c>0</c> to ignore cost in scoring. Default: <c>1.0</c>.
    /// </summary>
    public decimal CostWeight { get; init; } = 1.0m;

    /// <summary>
    /// Weight for speed tier (1–5, higher is faster).
    /// Set to <c>0</c> to ignore speed in scoring. Default: <c>1.0</c>.
    /// </summary>
    public decimal SpeedWeight { get; init; } = 1.0m;

    /// <summary>
    /// Weight for intelligence tier (1–5, higher is smarter).
    /// Set to <c>0</c> to ignore intelligence in scoring. Default: <c>1.0</c>.
    /// </summary>
    public decimal IntelligenceWeight { get; init; } = 1.0m;

    /// <summary>
    /// Computes the composite score for <paramref name="profile"/>.
    /// Higher scores are preferred.
    /// </summary>
    internal decimal Score(ModelProfile profile) =>
        -(profile.CostPer1KTokens * CostWeight)
        + (profile.SpeedTier * SpeedWeight)
        + (profile.IntelligenceTier * IntelligenceWeight);
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
public sealed class CapabilityModelRouter : IModelRouter, IModelCostResolver
{
    private readonly List<ModelProfile> _profiles = [];
    private readonly RoutingStrategy _strategy;
    private readonly RoutingWeights? _weights;
    private readonly Func<ModelProfile, decimal>? _scorer;
    private readonly ILogger _logger;
    private ModelProfile? _fallback;

    /// <summary>
    /// Model names already warned about for this process — avoids re-logging a deprecated
    /// model warning on every single routed request.
    /// </summary>
    private static readonly ConcurrentDictionary<string, byte> WarnedDeprecatedModels = new();

    /// <summary>Creates a router with a built-in strategy (CheapestFit, FastestFit, or BestFit).</summary>
    /// <param name="strategy">The built-in selection strategy.</param>
    /// <param name="logger">
    /// Optional logger used to warn once per process when routing selects a
    /// <see cref="ModelStatus.Deprecated"/> profile. Defaults to a no-op logger.
    /// </param>
    public CapabilityModelRouter(RoutingStrategy strategy = RoutingStrategy.CheapestFit, ILogger? logger = null)
    {
        _strategy = strategy;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Creates a router with the <see cref="RoutingStrategy.Weighted"/> strategy,
    /// balancing cost, speed, and intelligence according to <paramref name="weights"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// // Favour speed, tolerate higher cost:
    /// var router = new CapabilityModelRouter(new RoutingWeights
    /// {
    ///     CostWeight = 0.3m, SpeedWeight = 2.0m, IntelligenceWeight = 1.0m
    /// });
    /// </code>
    /// </example>
    public CapabilityModelRouter(RoutingWeights weights, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(weights);
        _strategy = RoutingStrategy.Weighted;
        _weights = weights;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Creates a router with a custom scoring function.
    /// The candidate with the <b>highest</b> score wins.
    /// </summary>
    /// <example>
    /// <code>
    /// // Custom scorer: prefer large context windows, penalise cost
    /// var router = new CapabilityModelRouter(p =>
    ///     p.MaxContextTokens / 100_000m - p.CostPer1KTokens * 2);
    /// </code>
    /// </example>
    public CapabilityModelRouter(Func<ModelProfile, decimal> scorer, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(scorer);
        _strategy = RoutingStrategy.Custom;
        _scorer = scorer;
        _logger = logger ?? NullLogger.Instance;
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
    public IAgentModel Select(AgentRequest request) => SelectProfile(request).Model;

    /// <inheritdoc />
    public ModelCostRates ResolveCostRates(AgentRequest request) => SelectProfile(request).GetCostRates();

    /// <summary>Wraps this router as an <see cref="IAgentModel"/> for use in <c>AgentJob</c>.</summary>
    public IAgentModel ToAgentModel() => new RoutedAgentModel(this);

    private ModelProfile SelectProfile(AgentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requirements = TaskRequirements.InferFrom(request);
        var candidates = _profiles.Where(p => p.Satisfies(requirements)).ToList();

        ModelProfile selected;
        if (candidates.Count == 0)
        {
            selected = _fallback
                ?? throw new InvalidOperationException(
                    $"No model satisfies the inferred requirements ({requirements.RequiredCapabilities}, " +
                    $"tier ≥ {requirements.MinIntelligenceTier}) and no fallback is configured. " +
                    $"Call WithFallback() or add a model with broader capabilities.");
        }
        else
        {
            selected = _strategy switch
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
                RoutingStrategy.Weighted when _weights is not null => candidates
                    .OrderByDescending(p => _weights.Score(p))
                    .First(),
                RoutingStrategy.Custom when _scorer is not null => candidates
                    .OrderByDescending(_scorer)
                    .First(),
                _ => candidates[0]
            };
        }

        WarnIfDeprecated(selected);
        return selected;
    }

    /// <summary>
    /// Logs a warning the first time this process routes to a
    /// <see cref="ModelStatus.Deprecated"/> profile. Subsequent selections of the same
    /// model name are silent — this is a one-time nudge, not per-request noise.
    /// </summary>
    private void WarnIfDeprecated(ModelProfile profile)
    {
        if (profile.Status != ModelStatus.Deprecated)
            return;

        if (!WarnedDeprecatedModels.TryAdd(profile.Name, 0))
            return;

        if (profile.ReplacedBy is { } replacement)
            _logger.LogWarning(
                "Routed to deprecated model '{Model}' — use '{ReplacedBy}' instead.",
                profile.Name, replacement);
        else
            _logger.LogWarning("Routed to deprecated model '{Model}'.", profile.Name);
    }
}
