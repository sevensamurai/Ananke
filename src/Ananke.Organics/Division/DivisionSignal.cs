using Ananke.Organics.Division.Approval;

namespace Ananke.Organics.Division;

/// <summary>
/// Observability event emitted during the division lifecycle.
/// Raised by <c>OrganicHost</c> at three points:
/// <list type="bullet">
///   <item><c>OnDivisionProposed</c> — policy says "divide"</item>
///   <item><c>OnDivisionApproved</c> — gate approved</item>
///   <item><c>OnDivisionRejected</c> — gate rejected</item>
/// </list>
/// These events are for logging and metrics only — the
/// <see cref="IDivisionApprovalGate"/> controls the actual flow.
/// </summary>
public sealed record DivisionSignal
{
    /// <summary>The workflow that should divide.</summary>
    public required string WorkflowName { get; init; }

    /// <summary>Complexity snapshot that triggered the signal.</summary>
    public required ComplexitySnapshot Snapshot { get; init; }

    /// <summary>Proposed division plan.</summary>
    public required DivisionPlan Plan { get; init; }

    /// <summary>When the signal was generated.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// The approval result from the <see cref="IDivisionApprovalGate"/>.
    /// <see langword="null"/> on <c>OnDivisionProposed</c> (gate hasn't been called yet);
    /// populated on <c>OnDivisionApproved</c> / <c>OnDivisionRejected</c>.
    /// </summary>
    public DivisionApproval? Approval { get; init; }
}
