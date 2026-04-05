namespace Ananke.Learning.Episodes;

/// <summary>
/// A completed episode — an ordered sequence of state→action transitions
/// ending in a terminal reward. Links <see cref="EmpiricalEntry"/> instances
/// into a trajectory for temporal credit assignment.
/// </summary>
public sealed record Episode
{
    /// <summary>Unique identifier for this episode.</summary>
    public required string Id { get; init; }

    /// <summary>Ordered steps forming the episode trajectory.</summary>
    public required IReadOnlyList<EpisodeStep> Steps { get; init; }

    /// <summary>Terminal reward received at the end of the episode.</summary>
    public required float TerminalReward { get; init; }

    /// <summary>When the episode began.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>When the episode completed.</summary>
    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>Optional metadata (e.g. opponent, move count, domain context).</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// The entity this episode belongs to, or <see langword="null"/>
    /// for global/unscoped episodes.
    /// </summary>
    public string? EntityId { get; init; }
}

/// <summary>
/// A single step in an episode trajectory.
/// </summary>
public sealed record EpisodeStep
{
    /// <summary>Zero-based position within the episode.</summary>
    public required int StepIndex { get; init; }

    /// <summary>The <see cref="EmpiricalEntry.Id"/> committed at this step.</summary>
    public required string EntryId { get; init; }

    /// <summary>Optional intermediate reward received at this step.</summary>
    public float IntermediateReward { get; init; }
}
