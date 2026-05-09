namespace Ananke.Abstractions.Tools;

/// <summary>
/// Semantic index of <see cref="ToolMemoryEntry"/> instances that backs the
/// JIT tool-injection gate.
/// </summary>
/// <remarks>
/// <para>
/// The default in-memory implementation (<c>InMemoryToolMemory</c>) uses keyword matching.
/// A Qdrant-backed implementation (<c>QdrantToolMemory</c>) provides dense-vector kNN
/// recall for large tool catalogues.
/// </para>
/// <para>
/// <see cref="RecallAsync"/> is the hot path: it is called by the tool gate (<c>IToolGate</c>)
/// on every model turn to produce the JIT-injected tool window.
/// </para>
/// <para>
/// Corresponds to the hippocampus role.
/// </para>
/// </remarks>
public interface IToolMemory
{
    /// <summary>
    /// Inserts or replaces the entry for a tool identified by
    /// (<paramref name="entry"/>.<see cref="ToolMemoryEntry.KitName"/>,
    /// <paramref name="entry"/>.<see cref="ToolMemoryEntry.ToolName"/>).
    /// </summary>
    Task UpsertAsync(ToolMemoryEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Removes the entry for the specified tool, if present.
    /// A no-op when the tool is not registered.
    /// </summary>
    Task RemoveAsync(string kitName, string toolName, CancellationToken ct = default);

    /// <summary>
    /// Returns up to <paramref name="topK"/> entries whose description or tags best match
    /// <paramref name="query"/>. Entries with <see cref="ToolHealth.Offline"/> health are
    /// excluded by default unless the caller explicitly overrides the filter.
    /// </summary>
    /// <param name="query">Natural-language query (usually the latest user message).</param>
    /// <param name="topK">Maximum number of entries to return.</param>
    /// <param name="tagFilter">
    /// When non-null, only entries sharing at least one tag are considered.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<ToolMemoryEntry>> RecallAsync(
        string query,
        int topK = 5,
        IReadOnlyList<string>? tagFilter = null,
        CancellationToken ct = default);

    /// <summary>
    /// Updates the <see cref="ToolHealth"/> of the named tool in-place.
    /// A no-op when the tool is not registered.
    /// </summary>
    Task MarkHealthAsync(string kitName, string toolName, ToolHealth health, CancellationToken ct = default);
}
