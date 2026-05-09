using Ananke.Learning;
using Ananke.Learning.EmpiricalMemory;

namespace Ananke.Organics.Division;

/// <summary>
/// Decorator that gives a cell domain-affine access to shared memory.
/// On commit, injects domain tags. On recall, biases toward domain-relevant
/// entries without excluding cross-domain knowledge.
/// </summary>
/// <remarks>
/// <para>
/// Unlike partitioning, this does NOT copy or isolate entries. The underlying
/// <see cref="IEmpiricalMemory"/> is shared across all cells. Each cell sees
/// the full memory but with domain-weighted recall priority.
/// </para>
/// <para>
/// Follows the same decorator pattern as
/// <see cref="Ananke.Learning.EntityMemory.EntityScopedEmpiricalMemory"/>.
/// </para>
/// </remarks>
/// <param name="inner">The shared empirical memory store.</param>
/// <param name="domainTags">Domain tags to inject on commit and use for recall bias.</param>
public sealed class DomainAffinityMemory(
    IEmpiricalMemory inner,
    IReadOnlyList<string> domainTags) : IEmpiricalMemory
{
    /// <summary>
    /// Commits with domain tags injected. The entry's existing tags are
    /// preserved; domain tags are appended if not already present.
    /// </summary>
    public Task<EmpiricalEntry> CommitAsync(EmpiricalEntry entry, CancellationToken ct = default)
    {
        var merged = entry.Tags.Union(domainTags).Distinct().ToList();
        return inner.CommitAsync(entry with { Tags = merged }, ct);
    }

    /// <summary>
    /// Recalls with domain tag bias. Does NOT set <c>RequiredTags</c> (which
    /// would exclude cross-domain entries). Instead, appends domain context to
    /// the situation query so the embedding naturally ranks domain-relevant
    /// entries higher.
    /// </summary>
    public Task<IReadOnlyList<EmpiricalMatch>> RecallAsync(
        string situation, RecallOptions? options = null, CancellationToken ct = default)
    {
        var enriched = $"{situation} [domain: {string.Join(", ", domainTags)}]";
        return inner.RecallAsync(enriched, options, ct);
    }

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
        inner.BrowseAsync(offset, limit, kind, entityId, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<EmpiricalEntry>> BrowseAsync(
        BrowseOptions options, CancellationToken ct = default) =>
        inner.BrowseAsync(options, ct);

    /// <inheritdoc />
    public Task<int> CountAsync(BrowseOptions? options = null, CancellationToken ct = default) =>
        inner.CountAsync(options, ct);

    /// <inheritdoc />
    public Task MarkConsolidatedAsync(string entryId, string knowledgeDocId, CancellationToken ct = default) =>
        inner.MarkConsolidatedAsync(entryId, knowledgeDocId, ct);
}
