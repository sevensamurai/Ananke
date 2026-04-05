using Ananke.Learning.Episodes;

namespace Ananke.Learning.EntityMemory;

/// <summary>
/// Decorator that scopes an <see cref="IEpisodeStore"/> to a specific entity
/// by injecting <see cref="Episode.EntityId"/> on commits and adding entity
/// filters on browse operations.
/// </summary>
/// <param name="inner">The shared episode store.</param>
/// <param name="entityId">The entity to scope to.</param>
public sealed class EntityScopedEpisodeStore(
    IEpisodeStore inner, string entityId) : IEpisodeStore
{
    private readonly string _entityId = entityId;

    /// <inheritdoc />
    public Task<Episode> CommitAsync(Episode episode, CancellationToken ct = default) =>
        inner.CommitAsync(episode with { EntityId = _entityId }, ct);

    /// <inheritdoc />
    public Task<Episode?> GetAsync(string episodeId, CancellationToken ct = default) =>
        inner.GetAsync(episodeId, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<Episode>> BrowseAsync(
        int offset, int limit, string? entityId = null,
        CancellationToken ct = default) =>
        inner.BrowseAsync(offset, limit, entityId ?? _entityId, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<Episode>> BrowseByOutcomeAsync(
        float minReward, float maxReward, int offset, int limit,
        string? entityId = null, CancellationToken ct = default) =>
        inner.BrowseByOutcomeAsync(minReward, maxReward, offset, limit,
            entityId ?? _entityId, ct);
}
