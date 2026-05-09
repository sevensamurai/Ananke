using System.Collections.Concurrent;

namespace Ananke.Organics.Kernel.Lineage;

/// <summary>
/// In-memory <see cref="ILineageStore"/>. All records are retained for the
/// lifetime of the process; death records update in place rather than delete.
/// </summary>
public sealed class InMemoryLineageStore : ILineageStore
{
    private readonly ConcurrentDictionary<string, CellLineage> _records = new();

    /// <inheritdoc />
    public Task RecordBirthAsync(CellLineage lineage, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lineage);
        _records[lineage.CellId] = lineage;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RecordDeathAsync(string cellId, DateTimeOffset diedAt, string reason, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cellId);

        if (_records.TryGetValue(cellId, out var existing))
            _records[cellId] = existing with { DiedAt = diedAt, DeathReason = reason };

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<CellLineage?> GetAsync(string cellId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cellId);
        _records.TryGetValue(cellId, out var record);
        return Task.FromResult(record);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CellLineage>> GetDescendantsAsync(string cellId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cellId);

        var result = new List<CellLineage>();
        CollectDescendants(cellId, result);
        return Task.FromResult<IReadOnlyList<CellLineage>>(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CellLineage>> GetByGenerationAsync(int generation, CancellationToken ct = default)
    {
        var result = _records.Values
            .Where(r => r.Generation == generation)
            .OrderBy(r => r.BornAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<CellLineage>>(result);
    }

    private void CollectDescendants(string parentId, List<CellLineage> accumulator)
    {
        foreach (var record in _records.Values)
        {
            if (record.ParentCellId == parentId)
            {
                accumulator.Add(record);
                CollectDescendants(record.CellId, accumulator);
            }
        }
    }
}
