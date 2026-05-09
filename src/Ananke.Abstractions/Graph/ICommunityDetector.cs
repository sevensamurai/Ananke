namespace Ananke.Abstractions.Graph;

/// <summary>
/// Assigns each node in a <see cref="IKnowledgeGraph"/> to an integer community label.
/// No default implementation is shipped in v1; register a concrete detector to enable
/// community-based features.
/// </summary>
public interface ICommunityDetector
{
    /// <summary>
    /// Returns a mapping of node ID → community label for every node in
    /// <paramref name="graph"/>.
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> DetectAsync(
        IKnowledgeGraph graph,
        CancellationToken ct = default);
}
