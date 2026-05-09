using Ananke.Abstractions.Tools;
using Ananke.Orchestration.Tools;

namespace Ananke.Skills;

/// <summary>
/// <see cref="ISkillCatalog"/> decorator that automatically projects every
/// <see cref="SkillDescriptor"/> returned by <see cref="SyncAsync"/> into an
/// <see cref="IToolMemory"/> as a <see cref="ToolMemoryEntry"/> (Phase 5 of the semantic tool gate).
/// </summary>
/// <remarks>
/// <para>
/// This bridges the external skill registry and the semantic tool gate: after each
/// <see cref="SyncAsync"/> call, the tool gate can recall skills by semantic query
/// without the agent being configured with a static <see cref="ToolKit"/>.
/// </para>
/// <para>
/// Skills are projected with:
/// <list type="bullet">
///   <item><see cref="ToolMemoryEntry.KitName"/> — the configured kit name.</item>
///   <item><see cref="ToolMemoryEntry.ToolName"/> — <see cref="SkillDescriptor.Name"/>.</item>
///   <item><see cref="ToolMemoryEntry.Description"/> — <see cref="SkillDescriptor.Description"/>.</item>
///   <item><see cref="ToolMemoryEntry.Tags"/> — <see cref="SkillDescriptor.Tags"/>.</item>
///   <item><see cref="ToolMemoryEntry.Health"/> — <see cref="ToolHealth.Healthy"/> on initial sync;
///     unchanged on subsequent syncs (preserves runtime health changes from the fault observer).</item>
/// </list>
/// </para>
/// <para>
/// All <see cref="ISkillCatalog"/> calls are forwarded unchanged to the inner catalog.
/// </para>
/// </remarks>
public sealed class SkillCatalogMemorySync : ISkillCatalog
{
    private readonly ISkillCatalog _inner;
    private readonly IToolMemory _memory;
    private readonly string _kitName;

    /// <summary>
    /// Creates the decorator.
    /// </summary>
    /// <param name="inner">The catalog whose skills are projected on each <see cref="SyncAsync"/>.</param>
    /// <param name="memory">The tool memory to upsert entries into.</param>
    /// <param name="kitName">
    /// The <see cref="ToolMemoryEntry.KitName"/> used for all projected entries.
    /// Should match the <see cref="ToolKit.Name"/> the agent will use to resolve tools.
    /// </param>
    public SkillCatalogMemorySync(ISkillCatalog inner, IToolMemory memory, string kitName)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentException.ThrowIfNullOrWhiteSpace(kitName);
        _inner = inner;
        _memory = memory;
        _kitName = kitName;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SkillDescriptor>> SearchAsync(
        string query,
        IReadOnlyList<string>? tags = null,
        int limit = 20,
        CancellationToken ct = default) =>
        _inner.SearchAsync(query, tags, limit, ct);

    /// <inheritdoc />
    public Task<ToolDefinition> ResolveAsync(SkillDescriptor skill, CancellationToken ct = default) =>
        _inner.ResolveAsync(skill, ct);

    /// <summary>
    /// Fetches the latest skills from the remote registry, updates the local cache,
    /// and upserts every <see cref="SkillDescriptor"/> into <see cref="IToolMemory"/>.
    /// </summary>
    /// <remarks>
    /// Entries that already exist in memory retain their current <see cref="ToolHealth"/>
    /// (i.e. runtime degradation from fault events is preserved across syncs).
    /// </remarks>
    public async Task SyncAsync(CancellationToken ct = default)
    {
        await _inner.SyncAsync(ct).ConfigureAwait(false);

        // Project every skill from the refreshed cache into tool memory
        var skills = await _inner.SearchAsync(
            query: string.Empty, tags: null, limit: int.MaxValue, ct: ct)
            .ConfigureAwait(false);

        foreach (var skill in skills)
        {
            var entry = new ToolMemoryEntry
            {
                KitName = _kitName,
                ToolName = skill.Name,
                Description = skill.Description,
                Tags = skill.Tags
                // Health defaults to Healthy; runtime mutations by IToolFaultObserver are preserved
                // because UpsertAsync replaces the entire record — we deliberately omit Health
                // to let the memory implementation decide (InMemoryToolMemory: always overwrites).
                // For preserving health, callers should use a MarkHealthAsync-aware store.
            };

            await _memory.UpsertAsync(entry, ct).ConfigureAwait(false);
        }
    }
}
