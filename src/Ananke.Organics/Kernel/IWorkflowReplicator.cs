using Ananke.Design;
using Ananke.Organics.Division;

namespace Ananke.Organics.Kernel;

/// <summary>
/// Replicates a living cell — spawns an identical clone from the same manifest.
/// The original keeps running. The clone shares the same domain, tools, and
/// memory. Used for scaling and redundancy, not specialization.
/// </summary>
/// <remarks>
/// <para>
/// Replication is the complement of division: division handles complexity
/// (too many tools → specialize); replication handles demand (too much load →
/// scale horizontally). Both use <c>ISkillPackager</c> for RNA seeding, but
/// replication seeds the full domain (no filtering to a subdomain).
/// </para>
/// </remarks>
public interface IWorkflowReplicator
{
    /// <summary>
    /// Clone a running cell. The original continues operating.
    /// </summary>
    /// <param name="sourceWorkflowName">The cell to clone.</param>
    /// <param name="cloneName">Name for the new clone (must be unique in the kernel).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The manifest and memory profile of the spawned clone.</returns>
    Task<ReplicationResult> ReplicateAsync(
        string sourceWorkflowName,
        string cloneName,
        CancellationToken ct = default);
}

/// <summary>Result of a successful cell replication.</summary>
public sealed record ReplicationResult
{
    /// <summary>The manifest used to spawn the clone (same as the source).</summary>
    public required WorkflowManifest Manifest { get; init; }

    /// <summary>Memory profile (same domains as the source).</summary>
    public required MemoryProfile MemoryProfile { get; init; }

    /// <summary>Name of the source cell that was cloned.</summary>
    public required string ClonedFrom { get; init; }
}
