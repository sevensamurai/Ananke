namespace Ananke.Federation.Recommendation;

/// <summary>
/// The aggregated result of a platform-fit evaluation for a workflow manifest.
/// Contains one <see cref="PlatformFitScore"/> per candidate platform, sorted
/// descending by <see cref="PlatformFitScore.Total"/>.
/// </summary>
public sealed record PlatformFitReport
{
    /// <summary>
    /// Per-platform scores, sorted descending by <see cref="PlatformFitScore.Total"/>.
    /// </summary>
    public required IReadOnlyList<PlatformFitScore> Scores { get; init; }

    /// <summary>
    /// The platform identifier of the top-ranked candidate, or <see langword="null"/>
    /// when every candidate was blocked.
    /// </summary>
    public required string? Recommended { get; init; }

    /// <summary>The weights that were used to compute the scores.</summary>
    public required RecommendationWeights Weights { get; init; }
}
