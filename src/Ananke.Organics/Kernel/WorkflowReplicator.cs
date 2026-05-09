using Ananke.Design;
using Ananke.Organics.Kernel.Snapshots;
using Ananke.Organics.Division;

namespace Ananke.Organics.Kernel;

/// <summary>
/// Default <see cref="IWorkflowReplicator"/> that clones a running cell by
/// deriving a snapshot from the source manifest and spawning it via
/// <see cref="IWorkflowHost"/>. The original keeps running — replication
/// is for horizontal scaling, not specialization.
/// </summary>
/// <remarks>
/// <para>
/// The clone shares the same tools, jobs, models, and domain as the source.
/// Its <see cref="MemoryProfile"/> matches the source's domains, and a
/// <see cref="DomainAffinityMemory"/> decorator is applied via the
/// <see cref="IWorkflowActivatorFactory"/> so it sees the same shared memory
/// with the same domain bias.
/// </para>
/// <para>
/// The clone's <see cref="WorkflowSnapshot.SplitFrom"/> is set to the source
/// name, enabling lineage tracking in the capability landscape.
/// </para>
/// </remarks>
public sealed class WorkflowReplicator(
    IWorkflowHost host,
    IWorkflowActivatorFactory activatorFactory,
    Func<string, WorkflowManifest> manifestFactory,
    Func<string, MemoryProfile>? memoryProfileFactory = null) : IWorkflowReplicator
{
    /// <inheritdoc />
    public async Task<ReplicationResult> ReplicateAsync(
        string sourceWorkflowName,
        string cloneName,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceWorkflowName);
        ArgumentException.ThrowIfNullOrWhiteSpace(cloneName);

        var manifest = manifestFactory(sourceWorkflowName);

        // Derive memory profile from the source, or create a default
        var profile = memoryProfileFactory?.Invoke(sourceWorkflowName)
            ?? new MemoryProfile { Domains = ["general"] };

        // Build a snapshot identical to the source but with the clone's name
        var snapshot = new WorkflowSnapshot
        {
            Name = cloneName,
            Domain = manifest.Name, // same domain as source
            SplitFrom = sourceWorkflowName,
            Tools = manifest.Jobs.Values
                .Where(j => j.Type.Equals("agent", StringComparison.OrdinalIgnoreCase))
                .Select(j => j.ModelAlias ?? "default")
                .Distinct()
                .ToList(),
            Connections = manifest.Connections.ToList(),
            Jobs = manifest.Jobs.ToDictionary(
                kv => kv.Key,
                kv => new JobSnapshot
                {
                    Type = kv.Value.Type,
                    ModelAlias = kv.Value.ModelAlias,
                    SystemPrompt = kv.Value.SystemPrompt,
                    MaxToolRounds = kv.Value.MaxToolRounds
                }),
            Models = manifest.Models.ToDictionary(
                kv => kv.Key,
                kv => new ModelSnapshot
                {
                    Provider = kv.Value.Provider,
                    Model = kv.Value.Model,
                    Endpoint = kv.Value.Endpoint
                }),
            MemoryProfile = profile
        };

        // Activate and spawn
        var loop = activatorFactory.CreateLoop(snapshot, profile);
        await host.StartAsync(cloneName, loop, ct);

        return new ReplicationResult
        {
            Manifest = manifest,
            MemoryProfile = profile,
            ClonedFrom = sourceWorkflowName
        };
    }
}
