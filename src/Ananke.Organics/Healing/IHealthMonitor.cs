using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;
using Ananke.Organics.Division;

namespace Ananke.Organics.Healing;

/// <summary>
/// Observes a workflow cell and computes both structural complexity metrics
/// (surface tension for division) and operational health metrics (fitness
/// for healing). Combines static analysis (manifest, tool definitions)
/// with execution telemetry (routing entropy, error rate, latency trends).
/// </summary>
/// <remarks>
/// <para>
/// Structural metrics (<see cref="ComplexitySnapshot.ToolCount"/>,
/// <see cref="ComplexitySnapshot.TagClusterCount"/>,
/// <see cref="ComplexitySnapshot.ResourceSpan"/>) can be computed from the
/// manifest and tool definitions alone. Telemetry metrics
/// (<see cref="ComplexitySnapshot.RoutingEntropy"/>,
/// <see cref="ComplexitySnapshot.ContextUtilization"/>) require recorded
/// executions.
/// </para>
/// <para>
/// Health metrics (<see cref="HealthSnapshot"/>) measure operational fitness:
/// error rate, latency/cost trends. These are reactive signals for the
/// healing policy, separate from the proactive complexity signals used
/// by the division policy.
/// </para>
/// </remarks>
public interface IHealthMonitor
{
    /// <summary>Record a completed execution for telemetry aggregation.</summary>
    void Record<TState>(WorkflowExecution<TState> execution);

    /// <summary>
    /// Compute the current complexity snapshot for a cell. Structural metrics
    /// are available immediately; telemetry metrics require prior
    /// <see cref="Record{TState}"/> calls.
    /// </summary>
    Task<ComplexitySnapshot> GetSnapshotAsync(string workflowName, CancellationToken ct = default);

    /// <summary>
    /// Compute health metrics for a cell. Requires recorded executions.
    /// Returns <see langword="null"/> if insufficient data (fewer than
    /// the minimum window size executions recorded).
    /// </summary>
    Task<HealthSnapshot?> GetHealthSnapshotAsync(string workflowName, CancellationToken ct = default);
}
