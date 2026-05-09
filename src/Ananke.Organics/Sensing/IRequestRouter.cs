namespace Ananke.Organics.Sensing;

/// <summary>
/// Infrastructure-level request router. Senses the capability landscape to
/// determine which cell should handle a message. This is NOT a workflow — it
/// is the routing function that lives outside all cells, like a reverse proxy
/// or message router.
/// </summary>
/// <remarks>
/// <para>
/// When multiple cells serve the same domain (replicas), the router
/// load-balances across them. When cells serve different domains (post-division),
/// the router dispatches by domain match.
/// </para>
/// </remarks>
public interface IRequestRouter
{
    /// <summary>
    /// Given a user message, sense the capability landscape and return the
    /// name of the cell that should handle it.
    /// </summary>
    Task<string> RouteAsync(string userMessage, CancellationToken ct = default);
}
