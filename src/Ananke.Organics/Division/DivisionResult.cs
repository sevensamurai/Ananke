using Ananke.Design;

namespace Ananke.Organics.Division;

/// <summary>
/// Result of a successful cell division. Contains the new manifests, routing
/// updates, and memory profiles for the spawned peer cells.
/// </summary>
public sealed record DivisionResult
{
    /// <summary>The new workflow manifests — peers, not children.</summary>
    public required IReadOnlyList<WorkflowManifest> NewManifests { get; init; }

    /// <summary>
    /// Triage routing table update (domain → cell name). Used by
    /// <see cref="Sensing.IRequestRouter"/> to route requests to the new cells.
    /// </summary>
    public required IReadOnlyDictionary<string, string> RoutingTable { get; init; }

    /// <summary>
    /// Memory domain profiles for each new cell. Used to create
    /// <see cref="DomainAffinityMemory"/> decorators.
    /// </summary>
    public required IReadOnlyList<MemoryProfile> MemoryProfiles { get; init; }
}
