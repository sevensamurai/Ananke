namespace Ananke.Organics.Division;

/// <summary>
/// Configuration for <see cref="WorkflowDivider"/> behavior.
/// </summary>
public sealed record DivisionOptions
{
    /// <summary>
    /// When <see langword="true"/>, the divider derives snapshots, manifests,
    /// and memory profiles but does not activate, spawn, or kill. Returns the
    /// <see cref="DivisionResult"/> for inspection without side effects.
    /// Default: <see langword="false"/>.
    /// </summary>
    public bool Simulate { get; init; }

    /// <summary>
    /// Timeout for child health confirmation via heartbeat polling.
    /// After all children are spawned, the divider polls the capability
    /// landscape for heartbeats. If any child hasn't signaled within this
    /// window, the division is aborted. Default: 10 seconds.
    /// </summary>
    public TimeSpan HealthConfirmationTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Trust scale for RNA seeding — inherited knowledge starts at this
    /// fraction of the parent's strength. Default: 0.8.
    /// </summary>
    public float SeedStrengthScale { get; init; } = 0.8f;

    /// <summary>
    /// Evidence source tag prefix for seeded entries. The full source is
    /// formatted as <c>"{SeedEvidenceSource}:{parentName}"</c>.
    /// Default: <c>"division-seed"</c>.
    /// </summary>
    public string SeedEvidenceSource { get; init; } = "division-seed";

    /// <summary>
    /// Optional transition orchestrator that manages the drain → switchover → complete
    /// lifecycle around the division. When <see langword="null"/>, the divider performs a
    /// stop-the-world switch with no drain or handoff guarantees.
    /// </summary>
    public IDivisionTransition? Transition { get; init; }
}
