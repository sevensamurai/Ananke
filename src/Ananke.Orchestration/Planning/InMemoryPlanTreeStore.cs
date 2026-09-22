using System.Collections.Concurrent;

namespace Ananke.Orchestration.Planning;

/// <summary>
/// Keeps plan trees in process memory. Tests and single-process runs; state is lost on restart.
/// </summary>
/// <remarks>
/// Trees are immutable records, so handing the same instance to two readers is safe and no copy is
/// made on the way out.
/// </remarks>
public sealed class InMemoryPlanTreeStore : IPlanTreeStore
{
    private readonly ConcurrentDictionary<string, PlanTree> _plans = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task SaveAsync(PlanTree tree, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tree);
        _plans[tree.PlanId] = tree;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PlanTree?> LoadAsync(string planId, CancellationToken ct = default) =>
        Task.FromResult(_plans.TryGetValue(planId, out var tree) ? tree : null);

    /// <inheritdoc />
    public Task DeleteAsync(string planId, CancellationToken ct = default)
    {
        _plans.TryRemove(planId, out _);
        return Task.CompletedTask;
    }
}
