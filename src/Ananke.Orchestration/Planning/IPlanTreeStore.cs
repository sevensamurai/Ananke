namespace Ananke.Orchestration.Planning;

/// <summary>
/// Where a plan tree lives between nodes.
/// </summary>
/// <remarks>
/// <para>
/// This is the store the whole design turns on. Because nodes read the tree instead of receiving
/// handoffs, the store <em>is</em> the channel — and it is also the surface an external observer
/// watches, since a monitor needs a structure to evaluate against rather than a stream of messages
/// it was never party to.
/// </para>
/// <para>
/// Built-in implementations: <see cref="InMemoryPlanTreeStore"/> for tests and single-process runs,
/// and <see cref="FilePlanTreeStore"/> for a tree that outlives the process. A distributed
/// deployment implements this over Redis, SQL or blob storage; serialization is
/// <see cref="System.Text.Json.JsonSerializer"/> and a suggested key is <c>plan:{planId}</c>.
/// </para>
/// <para>
/// Execution is sequential, so implementations may assume a single writer per plan.
/// </para>
/// </remarks>
public interface IPlanTreeStore
{
    /// <summary>Persists or overwrites the tree for <see cref="PlanTree.PlanId"/>.</summary>
    Task SaveAsync(PlanTree tree, CancellationToken ct = default);

    /// <summary>Loads a plan, or <see langword="null"/> when none exists.</summary>
    Task<PlanTree?> LoadAsync(string planId, CancellationToken ct = default);

    /// <summary>Deletes a plan and its whole lineage.</summary>
    Task DeleteAsync(string planId, CancellationToken ct = default);
}
