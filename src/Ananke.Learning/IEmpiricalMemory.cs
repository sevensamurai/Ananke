using Ananke.Abstractions.Memory;

namespace Ananke.Learning;

/// <summary>
/// Persistent store for empirical knowledge — observations, correlations,
/// and procedural strategies learned from agent-human collaboration or background
/// analysis. The third memory layer alongside
/// <see cref="Ananke.Orchestration.Knowledge.IKnowledgeStore"/> (semantic) and
/// <see cref="IConversationMemory"/> (episodic).
/// </summary>
/// <remarks>
/// <para>
/// Built-in implementations: <see cref="InMemoryEmpiricalMemory"/> (tests / single-process)
/// and <c>QdrantEmpiricalMemory</c> (distributed, in the <c>Ananke.Qdrant</c> package).
/// </para>
/// <para>
/// Entries are discriminated by <see cref="EmpiricalKind"/>:
/// <list type="bullet">
///   <item><see cref="EmpiricalKind.Pattern"/> — observational: "when X happens, Y follows"</item>
///   <item><see cref="EmpiricalKind.Skill"/> — procedural: "how to investigate X"</item>
///   <item><see cref="EmpiricalKind.Heuristic"/> — rules of thumb: "prefer X over Y in situation Z"</item>
/// </list>
/// </para>
/// </remarks>
public interface IEmpiricalMemory
{
    /// <summary>
    /// Stores a newly discovered empirical entry. If a semantically similar entry
    /// of the same <see cref="EmpiricalKind"/> already exists above the implementation's
    /// dedup threshold, the existing entry is reinforced instead of creating a duplicate.
    /// </summary>
    /// <returns>The committed entry — either the new entry or the merged existing one.</returns>
    Task<EmpiricalEntry> CommitAsync(EmpiricalEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Searches for empirical entries matching a situation description.
    /// Returns entries ranked by a composite score: relevance × confidence × recency.
    /// </summary>
    Task<IReadOnlyList<EmpiricalMatch>> RecallAsync(
        string situation, RecallOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Reinforces an existing entry — increments observation count, updates confidence,
    /// and records new evidence. Called when a known pattern is confirmed or a skill
    /// succeeds.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No entry with <paramref name="entryId"/> exists.</exception>
    Task ReinforceAsync(string entryId, Reinforcement reinforcement, CancellationToken ct = default);

    /// <summary>
    /// Weakens an entry that was found to be incorrect or ineffective.
    /// Does not delete — reduces confidence toward zero and records the reason.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No entry with <paramref name="entryId"/> exists.</exception>
    Task ContradictAsync(string entryId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Retrieves a specific entry by ID, or <see langword="null"/> if not found.
    /// </summary>
    Task<EmpiricalEntry?> GetAsync(string entryId, CancellationToken ct = default);

    /// <summary>
    /// Iterates entries in pages, optionally filtered by kind and/or entity.
    /// Used by background processes for decay sweeps and exploration.
    /// </summary>
    /// <param name="offset">Zero-based offset for paging.</param>
    /// <param name="limit">Maximum number of entries to return.</param>
    /// <param name="kind">When set, only entries of this kind are returned.</param>
    /// <param name="entityId">
    /// When set, only entries scoped to this entity are returned.
    /// When <see langword="null"/>, all entries (entity-scoped and global) are returned.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<EmpiricalEntry>> BrowseAsync(
        int offset, int limit, EmpiricalKind? kind = null,
        string? entityId = null, CancellationToken ct = default);

    /// <summary>
    /// Marks an entry as consolidated into a knowledge store document.
    /// Consolidated entries are excluded from future recall and exploration.
    /// </summary>
    /// <param name="entryId">The empirical entry to mark.</param>
    /// <param name="knowledgeDocId">The ID of the promoted knowledge document.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">No entry with <paramref name="entryId"/> exists.</exception>
    Task MarkConsolidatedAsync(string entryId, string knowledgeDocId, CancellationToken ct = default);
}
