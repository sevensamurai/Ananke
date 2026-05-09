namespace Ananke.Organics.Healing;

/// <summary>
/// Strategy for healing a degraded workflow cell.
/// </summary>
public enum HealingStrategy
{
    /// <summary>
    /// Kill the sick cell and spawn a replacement from a known-good snapshot.
    /// Used when the cell's configuration itself is bad (post-division regression).
    /// </summary>
    Rollback,

    /// <summary>
    /// Restart the cell with a fresh state (no rollback, just reset context).
    /// Used when context accumulation is the problem (conversation bloat).
    /// </summary>
    Restart,

    /// <summary>
    /// Remove the cell entirely and redistribute its domain to siblings.
    /// Used when the domain itself is unserviceable (tool permanently broken).
    /// </summary>
    Prune
}

/// <summary>
/// A healing recommendation produced by <see cref="IHealingPolicy"/>.
/// Describes what to heal and how.
/// </summary>
public sealed record HealingPlan
{
    /// <summary>Name of the cell to heal.</summary>
    public required string WorkflowName { get; init; }

    /// <summary>Recommended healing strategy.</summary>
    public required HealingStrategy Strategy { get; init; }

    /// <summary>
    /// Snapshot version to roll back to (for <see cref="HealingStrategy.Rollback"/>).
    /// <see langword="null"/> for other strategies.
    /// </summary>
    public int? TargetSnapshotVersion { get; init; }

    /// <summary>Human-readable reason for healing.</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// The health snapshot that triggered this plan. Used for diagnostics
    /// and learning (feeds into <c>IHealingOutcomeTracker</c>).
    /// </summary>
    public required HealthSnapshot TriggeringHealth { get; init; }
}
