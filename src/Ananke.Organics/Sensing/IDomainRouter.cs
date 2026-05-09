namespace Ananke.Organics.Sensing;

/// <summary>
/// Post-division prompt classifier. Given a user message, determines which
/// specialized child cell should handle it based on the tool-set split
/// produced by division.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="IRequestRouter"/> (replica load-balancing within a domain),
/// <see cref="IDomainRouter"/> routes <b>across</b> domains — it classifies
/// which domain a prompt belongs to and maps it to the owning cell.
/// </para>
/// <para>
/// The routing table is seeded by <see cref="Division.DivisionResult.RoutingTable"/>
/// and refined over time by <see cref="RoutingAffinityTracker"/> (adaptive
/// discovery phase).
/// </para>
/// </remarks>
public interface IDomainRouter
{
    /// <summary>
    /// Classify a user message and return the name of the cell that should
    /// handle it.
    /// </summary>
    /// <param name="userMessage">The user prompt to classify.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The workflow name of the best-matching cell.</returns>
    Task<string> RouteAsync(string userMessage, CancellationToken ct = default);

    /// <summary>
    /// Update the routing knowledge after a division event. Implementations
    /// index the child cells' tool descriptions for future classification.
    /// </summary>
    /// <param name="children">The child specifications from the division plan.</param>
    /// <param name="toolDescriptions">
    /// Tool name → description mapping. Used to build semantic representations
    /// of each child cell's capabilities.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task IndexAsync(
        IReadOnlyList<Division.ChildSpec> children,
        IReadOnlyDictionary<string, string> toolDescriptions,
        CancellationToken ct = default);
}
