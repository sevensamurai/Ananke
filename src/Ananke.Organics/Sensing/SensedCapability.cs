namespace Ananke.Organics.Sensing;

/// <summary>
/// A sensed capability — derived from cell signals, not from explicit
/// registration. Entries decay when the emitting cell stops signaling.
/// </summary>
public sealed record SensedCapability
{
    /// <summary>Name of the cell that last advertised this capability.</summary>
    public required string WorkflowName { get; init; }

    /// <summary>Domain this capability belongs to.</summary>
    public required string Domain { get; init; }

    /// <summary>Specific capabilities advertised (typically tool names).</summary>
    public required IReadOnlyList<string> Capabilities { get; init; }

    /// <summary>When this was last sensed (last heartbeat from the cell).</summary>
    public required DateTimeOffset LastSensed { get; init; }

    /// <summary>Whether this cell is considered alive (signal received within timeout).</summary>
    public required bool Alive { get; init; }
}
