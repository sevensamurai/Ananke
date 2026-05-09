using Ananke.Organics.Division;

namespace Ananke.Organics.Kernel.Snapshots;

/// <summary>
/// Point-in-time capture of an entire mesh — every cell's manifest, tools,
/// domains, memory profile, routing table, and division history. Designed for:
/// <list type="bullet">
///   <item><b>Rollback</b> — restore a kernel to a previous topology.</item>
///   <item><b>Deploy</b> — bootstrap a new mesh from a snapshot.</item>
///   <item><b>Diff</b> — compare before/after division to review changes.</item>
///   <item><b>Audit</b> — record the structural evolution of the organism.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// Snapshots are versioned. Each mutation (division, replication, tool addition)
/// produces a new version. The version counter is monotonic within a kernel.
/// </para>
/// <para>
/// Export to YAML via <see cref="HostSnapshotExporter.ToYaml"/> for storage,
/// diffing, or cross-deployment. Import back via <see cref="HostSnapshotExporter.FromYaml(string)"/>.
/// </para>
/// </remarks>
public sealed record HostSnapshot
{
    /// <summary>Unique identifier for this mesh (e.g. <c>"bookstore"</c>).</summary>
    public required string KernelId { get; init; }

    /// <summary>Monotonically increasing version within this mesh.</summary>
    public required int Version { get; init; }

    /// <summary>When this snapshot was taken.</summary>
    public required DateTimeOffset TakenAt { get; init; }

    /// <summary>All cells alive at snapshot time.</summary>
    public required IReadOnlyList<WorkflowSnapshot> Cells { get; init; }

    /// <summary>
    /// Domain → cell name routing table. Used by <see cref="Sensing.IRequestRouter"/>
    /// to dispatch requests after division.
    /// </summary>
    public IReadOnlyDictionary<string, string> RoutingTable { get; init; } =
        new Dictionary<string, string>();

    /// <summary>Ordered history of division events that produced this topology.</summary>
    public IReadOnlyList<DivisionRecord> DivisionHistory { get; init; } = [];
}

/// <summary>
/// Snapshot of a single cell — everything needed to reconstruct its workflow.
/// Combines the data from <see cref="Ananke.Design.WorkflowManifest"/> (topology,
/// models, jobs) with organic metadata (domain, tools, memory, lineage).
/// </summary>
public sealed record WorkflowSnapshot
{
    /// <summary>Cell name (unique within the kernel).</summary>
    public required string Name { get; init; }

    /// <summary>Primary domain this cell serves.</summary>
    public required string Domain { get; init; }

    /// <summary>
    /// Cell this was divided or cloned from. <see langword="null"/> for genesis cells.
    /// </summary>
    public string? SplitFrom { get; init; }

    /// <summary>Tool names available to this cell's agent jobs.</summary>
    public required IReadOnlyList<string> Tools { get; init; }

    /// <summary>DSL connection lines describing the workflow topology.</summary>
    public required IReadOnlyList<string> Connections { get; init; }

    /// <summary>Job declarations (name → definition).</summary>
    public required IReadOnlyDictionary<string, JobSnapshot> Jobs { get; init; }

    /// <summary>Model alias declarations (alias → provider + model).</summary>
    public required IReadOnlyDictionary<string, ModelSnapshot> Models { get; init; }

    /// <summary>Memory domain profile for domain-affine recall.</summary>
    public MemoryProfile? MemoryProfile { get; init; }
}

/// <summary>Snapshot of a job declaration within a cell.</summary>
public sealed record JobSnapshot
{
    /// <summary>Job type: <c>"agent"</c> or <c>"code"</c>.</summary>
    public required string Type { get; init; }

    /// <summary>Model alias (for agent jobs).</summary>
    public string? ModelAlias { get; init; }

    /// <summary>System prompt (for agent jobs).</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>Maximum tool-calling rounds.</summary>
    public int MaxToolRounds { get; init; } = 3;
}

/// <summary>Snapshot of a model alias declaration within a cell.</summary>
public sealed record ModelSnapshot
{
    /// <summary>Provider identifier (e.g. <c>"openai"</c>).</summary>
    public required string Provider { get; init; }

    /// <summary>Model name (e.g. <c>"gpt-4o-mini"</c>).</summary>
    public required string Model { get; init; }

    /// <summary>Optional custom endpoint URL.</summary>
    public string? Endpoint { get; init; }
}

/// <summary>
/// Record of a single division event in the kernel's history.
/// Used for audit trails and rollback decisions.
/// </summary>
public sealed record DivisionRecord
{
    /// <summary>The cell that was divided (now dead).</summary>
    public required string ParentWorkflow { get; init; }

    /// <summary>Cells that emerged from the division.</summary>
    public required IReadOnlyList<string> Children { get; init; }

    /// <summary>Reason the division was triggered.</summary>
    public required string Reason { get; init; }

    /// <summary>When the division occurred.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>Who approved the division (<c>"auto"</c>, user ID, <c>"llm-supervisor"</c>).</summary>
    public string? ApprovedBy { get; init; }
}
