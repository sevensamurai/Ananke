using System.Collections.Concurrent;
using Ananke.Abstractions.Graph;

namespace Ananke.Learning.Episodes;

/// <summary>
/// In-memory episode store for testing and single-process scenarios.
/// Episodes are stored in a concurrent dictionary; browse returns reverse
/// chronological order.
/// </summary>
public sealed class InMemoryEpisodeStore : IEpisodeStore
{
    private readonly ConcurrentDictionary<string, Episode> _episodes = new();
    // 5.7: Hard cap prevents unbounded heap growth in long-running scenarios.
    private readonly int _maxEpisodes;
    private readonly IEpisodeGraphProjector? _graphProjector;
    private readonly IKnowledgeGraph? _graph;

    /// <summary>
    /// Creates a new in-memory episode store.
    /// </summary>
    /// <param name="maxEpisodes">
    /// Maximum number of episodes retained. When the store is at capacity, the oldest
    /// episode (by <see cref="Episode.CompletedAt"/>) is evicted before the new one is
    /// written. Default is <c>50_000</c>.
    /// </param>
    /// <param name="graph">
    /// Optional knowledge graph. When both <paramref name="graph"/> and
    /// <paramref name="graphProjector"/> are supplied, every committed episode is
    /// projected into the graph immediately after the commit completes.
    /// </param>
    /// <param name="graphProjector">
    /// Strategy that translates an <see cref="Episode"/> into graph nodes/edges.
    /// Ignored when <paramref name="graph"/> is <see langword="null"/>.
    /// </param>
    public InMemoryEpisodeStore(
        int maxEpisodes = 50_000,
        IKnowledgeGraph? graph = null,
        IEpisodeGraphProjector? graphProjector = null)
    {
        if (maxEpisodes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxEpisodes), "Must be positive.");
        _maxEpisodes = maxEpisodes;
        _graph = graph;
        _graphProjector = graphProjector;
    }

    /// <inheritdoc />
    public async Task<Episode> CommitAsync(Episode episode, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(episode);

        // 5.7: Evict the oldest episode (by CompletedAt) before storing, if at capacity.
        if (!_episodes.ContainsKey(episode.Id) && _episodes.Count >= _maxEpisodes)
        {
            var oldest = _episodes.Values
                .OrderBy(e => e.CompletedAt)
                .Select(e => e.Id)
                .FirstOrDefault();
            if (oldest is not null)
                _episodes.TryRemove(oldest, out _);
        }

        _episodes[episode.Id] = episode;

        if (_graph is not null && _graphProjector is not null)
            await _graphProjector.ProjectAsync(episode, _graph, ct);

        return episode;
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
        int offset, int limit, string? entityId = null,
        CancellationToken ct = default)
    {
        var query = _episodes.Values.AsEnumerable();
        if (entityId is not null)
            query = query.Where(e => e.EntityId == entityId);

        var result = query
            .OrderByDescending(e => e.CompletedAt)
            .Skip(offset).Take(limit)
            .ToList();
        return Task.FromResult<IReadOnlyList<Episode>>(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Episode>> BrowseByOutcomeAsync(
        float minReward, float maxReward, int offset, int limit,
        string? entityId = null, CancellationToken ct = default)
    {
        var query = _episodes.Values
            .Where(e => e.TerminalReward >= minReward && e.TerminalReward <= maxReward);
        if (entityId is not null)
            query = query.Where(e => e.EntityId == entityId);

        var result = query
            .OrderByDescending(e => e.CompletedAt)
            .Skip(offset).Take(limit)
            .ToList();
        return Task.FromResult<IReadOnlyList<Episode>>(result);
    }
}
