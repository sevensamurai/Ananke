namespace Ananke.Organics.Division;

/// <summary>
/// High-level metabolic health signal for a workflow cell, derived from
/// execution telemetry (tokens, latency, error rate).
/// </summary>
/// <remarks>
/// Used by <see cref="MetabolicDivisionApprovalGate"/> to modulate whether
/// division proposals are auto-approved, gated on human review, or blocked.
/// </remarks>
public enum MetabolicSignal
{
    /// <summary>Normal operating range — no metabolic constraint on division.</summary>
    Healthy,

    /// <summary>
    /// Elevated resource use or error rate. Division proposals require
    /// human review even when an <c>AutoApprovalGate</c> is configured.
    /// </summary>
    Stressed,

    /// <summary>
    /// Critical resource exhaustion or sustained errors. Division proposals
    /// are rejected outright until metabolism recovers.
    /// </summary>
    Starved
}
