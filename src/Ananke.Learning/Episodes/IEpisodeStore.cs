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
    /// Iterates episodes in reverse chronological order (most recent first),
    /// optionally filtered by entity.
    /// </summary>
    /// <param name="offset">Zero-based offset for paging.</param>
    /// <param name="limit">Maximum number of episodes to return.</param>
    /// <param name="entityId">
    /// When set, only episodes scoped to this entity are returned.
    /// When <see langword="null"/>, all episodes are returned.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<Episode>> BrowseAsync(
        int offset, int limit, string? entityId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Iterates episodes filtered by terminal reward range, in reverse
    /// chronological order, optionally filtered by entity.
    /// </summary>
    /// <param name="minReward">Minimum terminal reward (inclusive).</param>
    /// <param name="maxReward">Maximum terminal reward (inclusive).</param>
    /// <param name="offset">Zero-based offset for paging.</param>
    /// <param name="limit">Maximum number of episodes to return.</param>
    /// <param name="entityId">
    /// When set, only episodes scoped to this entity are returned.
    /// When <see langword="null"/>, all episodes are returned.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<Episode>> BrowseByOutcomeAsync(
        float minReward, float maxReward, int offset, int limit,
        string? entityId = null, CancellationToken ct = default);
}
