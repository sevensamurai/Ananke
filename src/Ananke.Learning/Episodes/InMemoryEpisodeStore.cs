using System.Collections.Concurrent;

namespace Ananke.Learning.Episodes;

/// <summary>
/// In-memory episode store for testing and single-process scenarios.
/// Episodes are stored in a concurrent dictionary; browse returns reverse
/// chronological order.
/// </summary>
public sealed class InMemoryEpisodeStore : IEpisodeStore
{
    private readonly ConcurrentDictionary<string, Episode> _episodes = new();

    /// <inheritdoc />
    public Task<Episode> CommitAsync(Episode episode, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(episode);
        _episodes[episode.Id] = episode;
        return Task.FromResult(episode);
    }

    /// <inheritdoc />
    public Task<Episode?> GetAsync(string episodeId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(episodeId);
        _episodes.TryGetValue(episodeId, out var episode);
        return Task.FromResult(episode);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Episode>> BrowseAsync(
        int offset, int limit, CancellationToken ct = default)
    {
        var result = _episodes.Values
            .OrderByDescending(e => e.CompletedAt)
            .Skip(offset).Take(limit)
            .ToList();
        return Task.FromResult<IReadOnlyList<Episode>>(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Episode>> BrowseByOutcomeAsync(
        float minReward, float maxReward, int offset, int limit,
        CancellationToken ct = default)
    {
        var result = _episodes.Values
            .Where(e => e.TerminalReward >= minReward && e.TerminalReward <= maxReward)
            .OrderByDescending(e => e.CompletedAt)
            .Skip(offset).Take(limit)
            .ToList();
        return Task.FromResult<IReadOnlyList<Episode>>(result);
    }
}
