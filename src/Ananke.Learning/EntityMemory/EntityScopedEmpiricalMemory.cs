using Ananke.Learning.EmpiricalMemory;

namespace Ananke.Learning.EntityMemory;

/// <summary>
/// Decorator that scopes an <see cref="IEmpiricalMemory"/> to a specific entity
/// by injecting <see cref="EmpiricalEntry.EntityId"/> on commits and adding entity
/// filters on recall and browse operations.
/// </summary>
/// <param name="inner">The shared empirical memory store.</param>
/// <param name="entityId">The entity to scope to.</param>
public sealed class EntityScopedEmpiricalMemory(
    IEmpiricalMemory inner, string entityId) : IEmpiricalMemory
{
    private readonly string _entityId = entityId;

    /// <inheritdoc />
    public Task<EmpiricalEntry> CommitAsync(EmpiricalEntry entry, CancellationToken ct = default) =>
        inner.CommitAsync(entry with { EntityId = _entityId }, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<EmpiricalMatch>> RecallAsync(
        string situation, RecallOptions? options = null, CancellationToken ct = default) =>
        inner.RecallAsync(situation, (options ?? new RecallOptions()) with { EntityId = _entityId }, ct);

    /// <inheritdoc />
    public Task ReinforceAsync(string entryId, Reinforcement reinforcement, CancellationToken ct = default) =>
        inner.ReinforceAsync(entryId, reinforcement, ct);

    /// <inheritdoc />
    public Task ContradictAsync(string entryId, string reason, CancellationToken ct = default) =>
        inner.ContradictAsync(entryId, reason, ct);

    /// <inheritdoc />
    public Task<EmpiricalEntry?> GetAsync(string entryId, CancellationToken ct = default) =>
        inner.GetAsync(entryId, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<EmpiricalEntry>> BrowseAsync(
        int offset, int limit, EmpiricalKind? kind = null,
        string? entityId = null, CancellationToken ct = default) =>
        inner.BrowseAsync(offset, limit, kind, entityId ?? _entityId, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<EmpiricalEntry>> BrowseAsync(
        BrowseOptions options, CancellationToken ct = default) =>
        inner.BrowseAsync(options with { EntityId = options.EntityId ?? _entityId }, ct);

    /// <inheritdoc />
    public Task<int> CountAsync(BrowseOptions? options = null, CancellationToken ct = default) =>
        inner.CountAsync((options ?? new BrowseOptions()) with { EntityId = options?.EntityId ?? _entityId }, ct);

    /// <inheritdoc />
    public Task MarkConsolidatedAsync(string entryId, string knowledgeDocId, CancellationToken ct = default) =>
        inner.MarkConsolidatedAsync(entryId, knowledgeDocId, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<EmpiricalMatch>> PairRecallAsync(
        EmpiricalEntry reference,
        PairRecallOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new PairRecallOptions();
        var outerFilter = options.CandidateFilter;
        // Restrict candidates to this entity (or the reference's entity when scoped).
        return inner.PairRecallAsync(
            reference,
            options with
            {
                CandidateFilter = e =>
                    (e.EntityId == _entityId || e.EntityId is null)
                    && (outerFilter is null || outerFilter(e))
            },
            ct);
    }
}
