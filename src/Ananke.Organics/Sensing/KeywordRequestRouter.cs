namespace Ananke.Organics.Sensing;

/// <summary>
/// Simple keyword-based <see cref="IRequestRouter"/>. Scans the capability
/// landscape for alive cells whose domain appears as a substring in the user
/// message. When multiple cells serve the same domain, round-robins between
/// them. Falls back to the first alive cell when no domain keyword matches.
/// </summary>
/// <param name="landscape">The capability landscape to sense.</param>
public sealed class KeywordRequestRouter(ICapabilityMap landscape) : IRequestRouter
{
    private int _roundRobinCounter;

    /// <inheritdoc />
    public Task<string> RouteAsync(string userMessage, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);

        var alive = landscape.DiscoverAll();
        if (alive.Count == 0)
            throw new InvalidOperationException("No alive cells in the kernel.");

        // Find cells whose domain appears as a keyword in the message
        var matched = alive
            .Where(c => userMessage.Contains(c.Domain, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var candidates = matched.Count > 0 ? matched : alive;

        // Round-robin across candidates
        var index = Interlocked.Increment(ref _roundRobinCounter);
        var selected = candidates[((index % candidates.Count) + candidates.Count) % candidates.Count];

        return Task.FromResult(selected.WorkflowName);
    }
}
