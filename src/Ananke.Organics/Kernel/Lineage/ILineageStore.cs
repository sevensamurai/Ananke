namespace Ananke.Organics.Kernel.Lineage;

/// <summary>
/// Persistent store for <see cref="CellLineage"/> records. Records survive
/// cell death — callers must never delete a record when killing a cell.
/// </summary>
public interface ILineageStore
{
    /// <summary>Record the birth of a new cell.</summary>
    Task RecordBirthAsync(CellLineage lineage, CancellationToken ct = default);

    /// <summary>
    /// Mark an existing cell as dead. Does not remove the record.
    /// No-op if the cell is not found.
    /// </summary>
    Task RecordDeathAsync(string cellId, DateTimeOffset diedAt, string reason, CancellationToken ct = default);

    /// <summary>Retrieve the lineage record for a specific cell. Returns <see langword="null"/> if unknown.</summary>
    Task<CellLineage?> GetAsync(string cellId, CancellationToken ct = default);

    /// <summary>
    /// Return all descendants of <paramref name="cellId"/> (recursive) in
    /// birth order. Returns an empty list if none exist or the cell is unknown.
    /// </summary>
    Task<IReadOnlyList<CellLineage>> GetDescendantsAsync(string cellId, CancellationToken ct = default);

    /// <summary>Return all cells at a specific generation level.</summary>
    Task<IReadOnlyList<CellLineage>> GetByGenerationAsync(int generation, CancellationToken ct = default);
}
