using Ananke.Abstractions.Tools;

namespace Ananke.Orchestration.Tools.Gating;

/// <summary>
/// Keyword-based, in-process implementation of <see cref="IToolMemory"/>.
/// Recall is performed by counting term overlaps between the query and each entry's
/// description and tags — no embedding model required.
/// </summary>
/// <remarks>
/// This implementation is safe for use in unit tests and single-agent scenarios.
/// For large tool catalogues or cross-cell sharing, replace with <c>QdrantToolMemory</c>
/// (Phase 2 / Phase 5) which performs dense-vector kNN recall.
/// </remarks>
public sealed class InMemoryToolMemory : IToolMemory
{
    private readonly Dictionary<(string Kit, string Tool), ToolMemoryEntry> _store = [];
    private readonly Lock _lock = new();

    /// <inheritdoc />
    public Task UpsertAsync(ToolMemoryEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_lock)
            _store[(entry.KitName, entry.ToolName)] = entry;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string kitName, string toolName, CancellationToken ct = default)
    {
        lock (_lock)
            _store.Remove((kitName, toolName));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ToolMemoryEntry>> RecallAsync(
        string query,
        int topK = 5,
        IReadOnlyList<string>? tagFilter = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        IEnumerable<ToolMemoryEntry> candidates;
        lock (_lock)
            candidates = [.. _store.Values];

        // Exclude offline tools
        candidates = candidates.Where(e => e.Health != ToolHealth.Offline);

        // Apply tag pre-filter when requested
        if (tagFilter is { Count: > 0 })
        {
            var filterSet = new HashSet<string>(tagFilter, StringComparer.OrdinalIgnoreCase);
            candidates = candidates.Where(e => e.Tags.Any(t => filterSet.Contains(t)));
        }

        var queryTokens = Tokenize(query);

        var ranked = candidates
            .Select(e => (Entry: e, Score: Score(e, queryTokens)))
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Entry.HitCount)
            .Take(topK)
            .Select(x => x.Entry)
            .ToList();

        return Task.FromResult<IReadOnlyList<ToolMemoryEntry>>(ranked);
    }

    /// <inheritdoc />
    public Task MarkHealthAsync(string kitName, string toolName, ToolHealth health, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var key = (kitName, toolName);
            if (_store.TryGetValue(key, out var existing))
                _store[key] = existing with { Health = health };
        }
        return Task.CompletedTask;
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static HashSet<string> Tokenize(string text) =>
        new(text.Split([' ', '_', '-', '.', ',', '(', ')', '\n', '\r', '\t'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);

    private static int Score(ToolMemoryEntry entry, HashSet<string> queryTokens)
    {
        var score = 0;
        foreach (var token in Tokenize(entry.Description))
            if (queryTokens.Contains(token)) score++;
        foreach (var tag in entry.Tags)
            if (queryTokens.Contains(tag)) score += 2; // tags are weighted higher
        return score;
    }
}
