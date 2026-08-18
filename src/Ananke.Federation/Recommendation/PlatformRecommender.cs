using Ananke.Abstractions.Providers;
using Ananke.Design;
using Ananke.Federation.Monitoring;
using Ananke.Federation.Validation;
using Ananke.Orchestration.Tools;

namespace Ananke.Federation.Recommendation;

/// <summary>
/// Default implementation of <see cref="IPlatformRecommender"/>.
/// Reads <c>platform-capabilities.json</c> and <c>platform-profiles.json</c>
/// from the embedded resources in <c>Ananke.Federation</c>.
/// </summary>
/// <remarks>
/// Pass a <see cref="RemoteMetricsTracker"/> to <see cref="WithTelemetry"/> to enable
/// telemetry-calibrated cost and latency band overrides.
/// </remarks>
public sealed class PlatformRecommender : IPlatformRecommender
{
    private readonly RemoteMetricsTracker? _metricsTracker;

    /// <summary>Creates a recommender with optional telemetry calibration.</summary>
    /// <param name="metricsTracker">When supplied, live deployment telemetry overrides the cost/latency axis.</param>
    public PlatformRecommender(RemoteMetricsTracker? metricsTracker = null)
    {
        _metricsTracker = metricsTracker;
    }

    /// <summary>Returns a new recommender backed by the given tracker (fluent builder helper).</summary>
    public PlatformRecommender WithTelemetry(RemoteMetricsTracker tracker)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        return new PlatformRecommender(tracker);
    }
    // ── Band ordinals (low → high cost/latency) ───────────────────────
    private static readonly IReadOnlyDictionary<string, int> BandOrdinal =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["low"] = 0,
            ["medium"] = 1,
            ["high"] = 2
        };

    // ── IPlatformRecommender ──────────────────────────────────────────

    /// <inheritdoc />
    public PlatformFitReport Evaluate(
        WorkflowManifest manifest,
        ToolKit toolKit,
        IReadOnlyList<string>? candidatePlatforms = null,
        RecommendationWeights? weights = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(toolKit);

        weights ??= new RecommendationWeights();

        var platforms = ResolveCandidates(candidatePlatforms);

        var scores = platforms
            .Select(p => ScorePlatform(p, manifest, toolKit, weights, _metricsTracker))
            .OrderByDescending(s => s.Total)
            .ToList();

        var recommended = scores.FirstOrDefault(s => s.Total > 0)?.Platform;

        return new PlatformFitReport
        {
            Scores = scores,
            Recommended = recommended,
            Weights = weights
        };
    }

    /// <inheritdoc />
    public async Task<PlatformFitReport> EvaluateWithLiveValidationAsync(
        WorkflowManifest manifest,
        ToolKit toolKit,
        IReadOnlyList<IPlatformValidator> validators,
        IReadOnlyList<string>? candidatePlatforms = null,
        RecommendationWeights? weights = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(toolKit);
        ArgumentNullException.ThrowIfNull(validators);

        // Start with offline evaluation
        var baseReport = Evaluate(manifest, toolKit, candidatePlatforms, weights);

        if (validators.Count == 0)
            return baseReport;

        // Build a map of canonical platform → live validation result
        var liveResults = new Dictionary<string, DeployabilityReport>(StringComparer.OrdinalIgnoreCase);
        foreach (var validator in validators)
        {
            var canonicalPlatform = ResolveAlias(validator.Platform);
            // Only invoke validators for platforms we're evaluating
            if (!baseReport.Scores.Any(s => string.Equals(s.Platform, canonicalPlatform, StringComparison.OrdinalIgnoreCase)))
                continue;

            try
            {
                var liveReport = await validator.ValidateAsync(manifest, toolKit, ct);
                liveResults[canonicalPlatform] = liveReport;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Treat a validator contact failure as a warning, not a block
                liveResults[canonicalPlatform] = new DeployabilityReport
                {
                    Diagnostics =
                    [
                        new DeployDiagnostic
                        {
                            Severity  = DeployDiagnosticSeverity.Warning,
                            Code      = "FED900",
                            Message   = $"Live validation contact failed: {ex.Message}",
                            Component = validator.Platform,
                            Suggestion = "Check credentials and network connectivity."
                        }
                    ]
                };
            }
        }

        if (liveResults.Count == 0)
            return baseReport;

        // Overlay live diagnostics onto each score
        var revisedScores = baseReport.Scores
            .Select(s => OverlayLiveResults(s, liveResults))
            .OrderByDescending(s => s.Total)
            .ToList();

        var recommended = revisedScores.FirstOrDefault(s => s.Total > 0)?.Platform;

        return new PlatformFitReport
        {
            Scores = revisedScores,
            Recommended = recommended,
            Weights = baseReport.Weights
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Overlays live validation diagnostics onto an existing <see cref="PlatformFitScore"/>.
    /// Error diagnostics become Block reasons; warnings become Minus reasons.
    /// If any new Block is added the total is zeroed.
    /// </summary>
    private static PlatformFitScore OverlayLiveResults(
        PlatformFitScore score,
        IReadOnlyDictionary<string, DeployabilityReport> liveResults)
    {
        if (!liveResults.TryGetValue(score.Platform, out var live))
            return score;

        var extra = new List<FitReason>();

        foreach (var diag in live.Diagnostics)
        {
            extra.Add(new FitReason
            {
                Kind = diag.Severity == DeployDiagnosticSeverity.Error ? FitReasonKind.Block : FitReasonKind.Minus,
                Message = $"[live] {diag.Message}",
                Component = diag.Component,
                Capability = diag.Code
            });
        }

        if (extra.Count == 0)
            return score;

        List<FitReason> reasons = [.. score.Reasons, .. extra];
        var blocked = reasons.Any(r => r.Kind == FitReasonKind.Block);

        return score with
        {
            Total = blocked ? 0.0 : score.Total,
            Reasons = reasons
        };
    }

    /// <summary>
    /// Returns the canonical platform identifiers that would be evaluated for the given
    /// <paramref name="requested"/> list, resolving aliases. When <paramref name="requested"/>
    /// is <see langword="null"/> or empty all known platforms are returned.
    /// </summary>
    public static IReadOnlyList<string> KnownCanonicalPlatforms(IReadOnlyList<string>? requested = null)
        => ResolveCandidates(requested);

    private static IReadOnlyList<string> ResolveCandidates(IReadOnlyList<string>? requested)
    {
        if (requested is { Count: > 0 })
        {
            // Resolve aliases to canonical names
            return requested
                .Select(p => ResolveAlias(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return PlatformCapabilities.KnownPlatforms.ToList();
    }

    private static string ResolveAlias(string platform)
    {
        // Use the same alias map as DeployabilityValidator
        return platform.ToLowerInvariant() switch
        {
            "foundry" => "azure-ai",
            "gemini-enterprise" => "vertex-ai",
            _ => platform
        };
    }

    private static PlatformFitScore ScorePlatform(
        string platform,
        WorkflowManifest manifest,
        ToolKit toolKit,
        RecommendationWeights weights,
        RemoteMetricsTracker? metricsTracker = null)
    {
        var reasons = new List<FitReason>();

        var capScore = ScoreCapabilityCoverage(platform, toolKit, reasons);
        var strScore = ScoreStrengthAlignment(platform, manifest, reasons);
        var clScore = ScoreCostLatency(platform, manifest, reasons, metricsTracker);
        var govScore = ScoreGovernance(platform, manifest, reasons);

        // A Block reason zeroes the total
        var blocked = reasons.Any(r => r.Kind == FitReasonKind.Block);

        double total;
        if (blocked)
        {
            total = 0.0;
        }
        else
        {
            var totalWeight = weights.CapabilityWeight
                            + weights.StrengthWeight
                            + weights.CostLatencyWeight
                            + weights.GovernanceWeight;

            total = totalWeight == 0 ? 0 :
                (capScore * weights.CapabilityWeight
               + strScore * weights.StrengthWeight
               + clScore * weights.CostLatencyWeight
               + govScore * weights.GovernanceWeight)
               / totalWeight;
        }

        return new PlatformFitScore
        {
            Platform = platform,
            Total = Math.Round(total, 4),
            CapabilityCoverage = Math.Round(capScore, 4),
            StrengthAlignment = Math.Round(strScore, 4),
            CostLatencyFit = Math.Round(clScore, 4),
            GovernanceFit = Math.Round(govScore, 4),
            Reasons = reasons
        };
    }

    // ── Axis 1: capability coverage ───────────────────────────────────

    private static double ScoreCapabilityCoverage(
        string platform,
        ToolKit toolKit,
        List<FitReason> reasons)
    {
        var platformCaps = PlatformCapabilities.GetForPlatform(platform);

        // Identify required platform-native capabilities
        var required = toolKit.Tools.Values
            .Where(t => t.ExecutionMode == ToolExecutionMode.PlatformNative
                     && t.PlatformCapability is not null)
            .Select(t => (Tool: t.Name, Capability: t.PlatformCapability!))
            .ToList();

        // Tools that are Callback/Mcp/OpenApi always count as covered; no PlatformNative required
        var totalRequired = required.Count;

        if (totalRequired == 0)
            return 1.0;

        var covered = 0;
        foreach (var (toolName, cap) in required)
        {
            if (platformCaps.Contains(cap))
            {
                covered++;
                reasons.Add(new FitReason
                {
                    Kind = FitReasonKind.Plus,
                    Message = $"native: {cap}",
                    Capability = cap,
                    Component = toolName
                });
            }
            else
            {
                reasons.Add(new FitReason
                {
                    Kind = FitReasonKind.Minus,
                    Message = $"missing capability: {cap} (tool: {toolName})",
                    Capability = cap,
                    Component = toolName
                });
            }
        }

        return (double)covered / totalRequired;
    }

    // ── Axis 2: strength alignment ────────────────────────────────────

    private static double ScoreStrengthAlignment(
        string platform,
        WorkflowManifest manifest,
        List<FitReason> reasons)
    {
        if (manifest.Intents.Count == 0)
            return 0.5; // neutral

        var profile = PlatformProfiles.Get(platform);
        if (profile is null)
            return 0.5;

        var strengths = new HashSet<string>(profile.Strengths, StringComparer.OrdinalIgnoreCase);
        var weaknesses = new HashSet<string>(profile.Weaknesses, StringComparer.OrdinalIgnoreCase);

        double sum = 0;
        foreach (var tag in manifest.Intents)
        {
            if (strengths.Contains(tag))
            {
                sum += 1;
                reasons.Add(new FitReason
                {
                    Kind = FitReasonKind.Plus,
                    Message = $"{platform} is strong at {tag}"
                });
            }
            else if (weaknesses.Contains(tag))
            {
                sum -= 1;
                reasons.Add(new FitReason
                {
                    Kind = FitReasonKind.Minus,
                    Message = $"{platform} is weak at {tag}"
                });
            }
        }

        // Map [-count, +count] → [0, 1]
        var raw = sum / manifest.Intents.Count;
        return Math.Clamp((raw + 1.0) / 2.0, 0.0, 1.0);
    }

    // ── Axis 3: cost & latency fit ────────────────────────────────────

    private static double ScoreCostLatency(
        string platform,
        WorkflowManifest manifest,
        List<FitReason> reasons,
        RemoteMetricsTracker? metricsTracker = null)
    {
        var profile = PlatformProfiles.Get(platform);
        if (profile is null)
            return 0.5;

        // P6: Apply telemetry calibration if tracker data is available
        var costBand = profile.CostBand;
        var latencyBand = profile.LatencyBand;
        ApplyTelemetryCalibration(platform, metricsTracker, ref costBand, ref latencyBand, reasons);

        double score = 1.0;
        const double penaltyPerBand = 0.25;

        // Cost band
        if (manifest.Budget?.MaxCostPerRunUsd is { } maxCost)
        {
            // Map the platform band to a rough per-run estimate so we can compare
            var expectedCost = costBand.ToLowerInvariant() switch
            {
                "low" => 0.10,
                "medium" => 0.45,
                "high" => 1.00,
                _ => 0.45
            };

            if (expectedCost > maxCost)
            {
                var dist = BandDistance(costBand, "low");
                score -= dist * penaltyPerBand;
                reasons.Add(new FitReason
                {
                    Kind = FitReasonKind.Minus,
                    Message = $"cost band '{costBand}' may exceed budget ${maxCost:F2}/run"
                });
            }
        }

        // Latency band
        if (manifest.Slo?.LatencyP50Ms is { } maxLatency)
        {
            var expectedLatency = latencyBand.ToLowerInvariant() switch
            {
                "low" => 800,
                "medium" => 2000,
                "high" => 4000,
                _ => 2000
            };

            if (expectedLatency > maxLatency)
            {
                var dist = BandDistance(latencyBand, "low");
                score -= dist * penaltyPerBand;
                reasons.Add(new FitReason
                {
                    Kind = FitReasonKind.Minus,
                    Message = $"latency band '{latencyBand}' may exceed SLO {maxLatency}ms p50"
                });
            }
        }

        return Math.Clamp(score, 0.0, 1.0);
    }

    /// <summary>
    /// Overrides <paramref name="costBand"/> and <paramref name="latencyBand"/> with
    /// measured telemetry when the <see cref="RemoteMetricsTracker"/> has sufficient
    /// samples for any deployment on this platform.  Adds a Plus reason noting
    /// "calibrated from telemetry" so the score is transparent.
    /// </summary>
    private static void ApplyTelemetryCalibration(
        string platform,
        RemoteMetricsTracker? tracker,
        ref string costBand,
        ref string latencyBand,
        List<FitReason> reasons)
    {
        if (tracker is null)
            return;

        // Find the first deployment whose ID starts with the platform id —
        // by convention deployment IDs are "<platform>/<cell>" or "<platform>-<cell>".
        var trackable = tracker.GetTrackableDeployments();
        var match = trackable.FirstOrDefault(id =>
            id.StartsWith(platform, StringComparison.OrdinalIgnoreCase));

        if (match is null)
            return;

        var trend = tracker.GetTrend(match);
        if (trend is null)
            return;

        // Rising token slope → nudge cost band up; falling → nudge down
        if (trend.TokensPerExecutionSlope > 0.10)
        {
            costBand = BumpBandUp(costBand);
            reasons.Add(new FitReason
            {
                Kind = FitReasonKind.Minus,
                Message = $"cost band adjusted to '{costBand}' (calibrated from telemetry: tokens trending up)"
            });
        }
        else if (trend.TokensPerExecutionSlope < -0.10)
        {
            costBand = BumpBandDown(costBand);
            reasons.Add(new FitReason
            {
                Kind = FitReasonKind.Plus,
                Message = $"cost band adjusted to '{costBand}' (calibrated from telemetry: tokens trending down)"
            });
        }
        else
        {
            reasons.Add(new FitReason
            {
                Kind = FitReasonKind.Plus,
                Message = $"cost/latency calibrated from telemetry ({trend.SampleCount} samples, stable)"
            });
        }
    }

    private static string BumpBandUp(string band) => band.ToLowerInvariant() switch
    {
        "low" => "medium",
        _ => "high"
    };

    private static string BumpBandDown(string band) => band.ToLowerInvariant() switch
    {
        "high" => "medium",
        _ => "low"
    };

    private static int BandDistance(string from, string to)
    {
        var a = BandOrdinal.TryGetValue(from, out var av) ? av : 1;
        var b = BandOrdinal.TryGetValue(to, out var bv) ? bv : 1;
        return Math.Abs(a - b);
    }

    // ── Axis 4: governance fit ────────────────────────────────────────

    private static double ScoreGovernance(
        string platform,
        WorkflowManifest manifest,
        List<FitReason> reasons)
    {
        var gov = manifest.Governance;
        if (gov is null)
            return 1.0; // neutral

        var profile = PlatformProfiles.Get(platform);
        if (profile is null)
            return 0.5;

        var flags = profile.GovernanceFlags;
        var allSatisfied = true;

        if (gov.Rbac)
        {
            if (flags.Contains("rbac") || flags.Contains("workspacesRbac") || flags.Contains("iamConditions"))
                reasons.Add(new FitReason { Kind = FitReasonKind.Plus, Message = "governance: RBAC supported" });
            else
            {
                allSatisfied = false;
                reasons.Add(new FitReason { Kind = FitReasonKind.Block, Message = "governance: RBAC required but not supported" });
            }
        }

        if (gov.PrivateNetworking)
        {
            if (flags.Contains("privateNetworking") || flags.Contains("vpcServiceControls"))
                reasons.Add(new FitReason { Kind = FitReasonKind.Plus, Message = "governance: private networking supported" });
            else
            {
                allSatisfied = false;
                reasons.Add(new FitReason { Kind = FitReasonKind.Block, Message = "governance: private networking required but not supported" });
            }
        }

        if (gov.ContentSafety)
        {
            if (flags.Contains("contentSafety"))
                reasons.Add(new FitReason { Kind = FitReasonKind.Plus, Message = "governance: content safety supported" });
            else
            {
                allSatisfied = false;
                reasons.Add(new FitReason { Kind = FitReasonKind.Minus, Message = "governance: content safety required but not declared" });
            }
        }

        if (gov.Region is not null)
        {
            var regions = profile.Regions;
            if (regions.Contains("global", StringComparer.OrdinalIgnoreCase) ||
                regions.Any(r => r.StartsWith(gov.Region, StringComparison.OrdinalIgnoreCase)))
                reasons.Add(new FitReason { Kind = FitReasonKind.Plus, Message = $"governance: region '{gov.Region}' available" });
            else
            {
                allSatisfied = false;
                reasons.Add(new FitReason { Kind = FitReasonKind.Minus, Message = $"governance: region '{gov.Region}' not listed for this platform" });
            }
        }

        return allSatisfied ? 1.0 : 0.0;
    }
}
