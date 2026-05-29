using System.Collections.Concurrent;

namespace Ananke.Organics.Division.Review;

/// <summary>
/// In-process, thread-safe implementation of <see cref="IWorkReviewParkingStore"/>.
/// All state is held in memory; entries are lost on process restart.
/// </summary>
/// <remarks>
/// Use this implementation for single-process deployments and tests. For durable
/// storage across restarts or multiple replicas, a Redis-backed counterpart is planned
/// for a later release.
/// </remarks>
public sealed class InMemoryWorkReviewParkingStore : IWorkReviewParkingStore
{
    private readonly ConcurrentDictionary<string, (WorkItem Item, string GateId)> _store = new();

    /// <inheritdoc />
    public Task<string> ParkAsync(WorkItem item, string gateId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(gateId);

        var id = Guid.NewGuid().ToString("N");
        _store[id] = (item, gateId);
        return Task.FromResult(id);
    }

    /// <inheritdoc />
    public Task<(WorkItem Item, string GateId)?> TryGetAsync(string parkingId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parkingId);

        (WorkItem Item, string GateId)? result =
            _store.TryGetValue(parkingId, out var entry) ? entry : null;

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task CompleteAsync(string parkingId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parkingId);

        _store.TryRemove(parkingId, out _);
        return Task.CompletedTask;
    }
}
