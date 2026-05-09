namespace Ananke.Organics.Sensing;

/// <summary>
/// Periodic heartbeat emitted by a living cell. the kernel's nervous system
/// aggregates these into a capability landscape. No signal for a configured
/// duration means the cell is assumed dead.
/// </summary>
/// <remarks>
/// <para>
/// A cell emits this every few seconds as part of its normal loop — it is the
/// biological equivalent of surface receptor expression. The cell doesn't
/// "register" its capabilities; they're continuously expressed, and the
/// organism senses them.
/// </para>
/// <para>
/// For the in-process mesh model, signals are passed directly via
/// <see cref="ICapabilityMap"/>. For multi-process models
/// (Docker, K8s), signals flow through <c>IHandoffChannel</c> on a
/// well-known topic.
/// </para>
/// </remarks>
public sealed record WorkflowSignal
{
    /// <summary>Name of the emitting cell.</summary>
    public required string WorkflowName { get; init; }

    /// <summary>Primary domain this cell serves (e.g. <c>"search"</c>, <c>"payment"</c>).</summary>
    public required string Domain { get; init; }

    /// <summary>Capabilities this cell currently advertises (typically tool names).</summary>
    public required IReadOnlyList<string> Capabilities { get; init; }

    /// <summary>When this signal was emitted.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Lineage — which cell this was divided or cloned from, if any.</summary>
    public string? SplitFrom { get; init; }
}
