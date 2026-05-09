namespace Ananke.Organics.Division;

/// <summary>
/// Memory domain profile — the cell's "expressed genes." Declares which memory
/// domains a cell affiliates with, biasing its recall toward domain-relevant
/// entries without excluding cross-domain knowledge.
/// </summary>
/// <remarks>
/// <para>
/// On division, each child inherits a subset of the parent's domains plus
/// shared lineage tags. For example, if <c>bookstore-general</c> had domains
/// <c>["search", "payment", "general"]</c>, then after division:
/// </para>
/// <list type="bullet">
///   <item><c>bookstore-browse</c> inherits <c>["search", "general"]</c> + lineage <c>["bookstore"]</c></item>
///   <item><c>bookstore-orders</c> inherits <c>["payment", "general"]</c> + lineage <c>["bookstore"]</c></item>
/// </list>
/// </remarks>
public sealed record MemoryProfile
{
    /// <summary>
    /// Primary domains this cell reads/writes. Injected on commit and used
    /// for recall bias by <see cref="DomainAffinityMemory"/>.
    /// </summary>
    public required IReadOnlyList<string> Domains { get; init; }

    /// <summary>
    /// Inherited lineage tags — shared by all cells divided from the same
    /// ancestor. Enables cross-cell knowledge discovery.
    /// </summary>
    public IReadOnlyList<string> LineageTags { get; init; } = [];
}
