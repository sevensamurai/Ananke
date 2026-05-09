namespace Ananke.Federation.Recommendation;

/// <summary>
/// Composite fit score for a single platform, comprising four independent axes
/// each in <c>[0, 1]</c> combined into a weighted <see cref="Total"/>.
/// A <see cref="FitReasonKind.Block"/> reason zeroes <see cref="Total"/> regardless
/// of the individual axis scores.
/// </summary>
public sealed record PlatformFitScore
{
    /// <summary>Canonical platform identifier (e.g. <c>"azure-ai"</c>).</summary>
    public required string Platform { get; init; }

    /// <summary>Weighted total score in <c>[0, 1]</c>. Zero when any <see cref="FitReasonKind.Block"/> reason is present.</summary>
    public required double Total { get; init; }

    /// <summary>Fraction of required <c>PlatformNative</c> capabilities supported by the platform.</summary>
    public required double CapabilityCoverage { get; init; }

    /// <summary>
    /// How well the manifest's intent tags align with the platform's declared strengths
    /// and weaknesses. Neutral (0.5) when no intent tags are declared.
    /// </summary>
    public required double StrengthAlignment { get; init; }

    /// <summary>Closeness of the manifest's budget / SLO hints to the platform's cost and latency bands.</summary>
    public required double CostLatencyFit { get; init; }

    /// <summary>
    /// Whether all governance requirements in the manifest are satisfied by the platform.
    /// 1.0 when no governance requirements are declared (neutral).
    /// </summary>
    public required double GovernanceFit { get; init; }

    /// <summary>Ordered list of reasons that drove the score up, down, or blocked it.</summary>
    public required IReadOnlyList<FitReason> Reasons { get; init; }
}
