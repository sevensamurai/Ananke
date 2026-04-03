using System.Collections.Concurrent;

namespace Ananke.Orchestration.Knowledge.Linking;

/// <summary>
/// In-memory <see cref="IDocumentLinkGraph"/> for testing and single-process scenarios.
/// Stores directed links in a thread-safe dictionary keyed by source chunk ID.
/// </summary>
public sealed class InMemoryDocumentLinkGraph : IDocumentLinkGraph
{
    // source → (target → link)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DocumentLink>> _outbound = new();

    /// <inheritdoc />
    public Task AddLinkAsync(
        string sourceChunkId,
        string targetChunkId,
        string relationship,
        float weight = 1.0f,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceChunkId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetChunkId);
        ArgumentException.ThrowIfNullOrWhiteSpace(relationship);

        var link = new DocumentLink
        {
            SourceId = sourceChunkId,
            TargetId = targetChunkId,
            Relationship = relationship,
            Weight = Math.Clamp(weight, 0f, 1f)
        };

        var targets = _outbound.GetOrAdd(sourceChunkId, _ => new ConcurrentDictionary<string, DocumentLink>());
        targets[targetChunkId] = link;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DocumentLink>> GetLinksAsync(
        string chunkId, int maxHops = 1, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chunkId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHops);

        var result = new List<DocumentLink>();
        var visited = new HashSet<string> { chunkId };
        var frontier = new Queue<string>();
        frontier.Enqueue(chunkId);

        for (var hop = 0; hop < maxHops && frontier.Count > 0; hop++)
        {
            var nextFrontier = new Queue<string>();

            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();

                if (!_outbound.TryGetValue(current, out var targets))
                    continue;

                foreach (var link in targets.Values)
                {
                    result.Add(link);

                    if (visited.Add(link.TargetId))
                        nextFrontier.Enqueue(link.TargetId);
                }
            }

            frontier = nextFrontier;
        }

        return Task.FromResult<IReadOnlyList<DocumentLink>>(result);
    }

    /// <inheritdoc />
    public Task RemoveLinksAsync(string chunkId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chunkId);

        // Remove outbound links from this chunk
        _outbound.TryRemove(chunkId, out _);

        // Remove inbound links to this chunk from all other sources
        foreach (var (_, targets) in _outbound)
            targets.TryRemove(chunkId, out _);

        return Task.CompletedTask;
    }

    /// <summary>Returns the total number of links stored in the graph.</summary>
    public int LinkCount => _outbound.Values.Sum(t => t.Count);
}
