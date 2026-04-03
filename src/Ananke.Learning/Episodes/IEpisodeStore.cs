namespace Ananke.Learning.Episodes;

/// <summary>
/// Persistence contract for completed episodes. Stores ordered trajectories
/// of decisions linked to terminal outcomes, enabling temporal credit
/// assignment and skill packaging.
/// </summary>
public interface IEpisodeStore
{
    /// <summary>
    /// Commits a completed episode. If an episode with the same
    /// <see cref="Episode.Id"/> already exists, it is replaced.
    /// </summary>
    Task<Episode> CommitAsync(Episode episode, CancellationToken ct = default);

    /// <summary>
    /// Retrieves an episode by ID, or <see langword="null"/> if not found.
    /// </summary>
    Task<Episode?> GetAsync(string episodeId, CancellationToken ct = default);

    /// <summary>
    /// Iterates episodes in reverse chronological order (most recent first).
    /// </summary>
    Task<IReadOnlyList<Episode>> BrowseAsync(
        int offset, int limit, CancellationToken ct = default);

    /// <summary>
    /// Iterates episodes filtered by terminal reward range, in reverse
    /// chronological order.
    /// </summary>
    Task<IReadOnlyList<Episode>> BrowseByOutcomeAsync(
        float minReward, float maxReward, int offset, int limit,
        CancellationToken ct = default);
}
