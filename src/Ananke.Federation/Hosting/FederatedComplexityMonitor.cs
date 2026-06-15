using Ananke.Design;
using Ananke.Federation.Deployment;
using Ananke.Federation.Monitoring;
using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;
using Ananke.Organics.Division;
using Ananke.Organics.Healing;

namespace Ananke.Federation.Hosting;

/// <summary>
/// Composite <see cref="IHealthMonitor"/> that produces
/// <see cref="ComplexitySnapshot"/> for both local and remote cells.
/// </summary>
/// <remarks>
/// <para>
/// For <b>local cells</b>, delegates to the underlying local monitor which has
/// full telemetry (routing entropy, context utilization).
/// </para>
/// <para>
/// For <b>remote cells</b>, computes structural metrics from the deployed manifest
/// (tool count, job count) and enriches with platform telemetry when available via
/// <see cref="IRemoteCellMonitor"/>. Routing entropy remains 0.0 for remote cells
/// because platforms do not expose per-tool call distribution.
/// </para>
/// </remarks>
public sealed class FederatedComplexityMonitor : IHealthMonitor, IRemoteCellSource
{
    private readonly IHealthMonitor _localMonitor;
    private readonly IDeploymentRegistry _registry;
    private readonly IReadOnlyList<IRemoteCellMonitor> _remoteMonitors;
    private readonly IReadOnlyDictionary<string, WorkflowManifest> _manifests;
    private readonly RemoteMetricsTracker _metricsTracker;

    /// <summary>
    /// Creates a federated complexity monitor.
    /// </summary>
    /// <param name="localMonitor">The local health monitor for in-process cells.</param>
    /// <param name="registry">Deployment registry to identify remote cells.</param>
    /// <param name="remoteMonitors">Platform-specific remote cell monitors.</param>
    /// <param name="manifests">
    /// Known manifests keyed by workflow name. Used to compute structural metrics
    /// for remote cells.
    /// </param>
    /// <param name="metricsTracker">
    /// Optional metrics tracker for trend detection. When provided, each
    /// remote snapshot poll records a sample for trend analysis.
    /// </param>
    public FederatedComplexityMonitor(
        IHealthMonitor localMonitor,
        IDeploymentRegistry registry,
        IReadOnlyList<IRemoteCellMonitor>? remoteMonitors = null,
        IReadOnlyDictionary<string, WorkflowManifest>? manifests = null,
        RemoteMetricsTracker? metricsTracker = null)
    {
        ArgumentNullException.ThrowIfNull(localMonitor);
        ArgumentNullException.ThrowIfNull(registry);

        _localMonitor = localMonitor;
        _registry = registry;
        _remoteMonitors = remoteMonitors ?? [];
        _manifests = manifests ?? new Dictionary<string, WorkflowManifest>();
        _metricsTracker = metricsTracker ?? new RemoteMetricsTracker();
    }

    /// <summary>
    /// The metrics tracker used for trend detection on remote cells.
    /// Query <see cref="RemoteMetricsTracker.GetTrend"/> for per-deployment trends.
    /// </summary>
    public RemoteMetricsTracker MetricsTracker => _metricsTracker;

    /// <inheritdoc />
    public void Record<TState>(WorkflowExecution<TState> execution)
    {
        // Only local executions produce telemetry — remote cells are opaque.
        _localMonitor.Record(execution);
    }

    /// <inheritdoc />
    public async Task<ComplexitySnapshot> GetSnapshotAsync(string workflowName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);

        var deployments = await _registry.ListAsync(workflowName, ct);
        var activeDeployment = deployments.FirstOrDefault(d => d.Status == DeploymentStatus.Active);

        if (activeDeployment is null)
            return await _localMonitor.GetSnapshotAsync(workflowName, ct);

        return await BuildRemoteSnapshotAsync(workflowName, activeDeployment, ct);
    }

    /// <inheritdoc />
    public Task<HealthSnapshot?> GetHealthSnapshotAsync(string workflowName, CancellationToken ct = default) =>
        _localMonitor.GetHealthSnapshotAsync(workflowName, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetRemoteCellNamesAsync(CancellationToken ct = default)
    {
        var all = await _registry.ListAsync(ct: ct);
        return all
            .Where(d => d.Status == DeploymentStatus.Active)
            .Select(d => d.WorkflowName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<ComplexitySnapshot> BuildRemoteSnapshotAsync(
        string workflowName, DeploymentRecord deployment, CancellationToken ct)
    {
        var (toolCount, jobCount) = GetStructuralMetrics(workflowName);

        var monitor = _remoteMonitors.FirstOrDefault(m =>
            string.Equals(m.Platform, deployment.Platform, StringComparison.OrdinalIgnoreCase));

        float avgLatencyMs = 0f;
        decimal avgCost = 0m;

        if (monitor is not null)
        {
            try
            {
                var metrics = await monitor.GetMetricsAsync(deployment.DeploymentId, ct);
                var health = await monitor.GetHealthAsync(deployment.DeploymentId, ct);

                avgLatencyMs = (float)health.LatencyMs;
                if (metrics.ExecutionCount > 0)
                    avgCost = (decimal)metrics.TotalTokens / metrics.ExecutionCount * 0.001m;

                _metricsTracker.Record(metrics);
            }
            catch (Exception)
            {
                // Best-effort — remote platform may be unavailable.
            }
        }

        return new ComplexitySnapshot
        {
            WorkflowName = workflowName,
            ToolCount = toolCount,
            JobCount = jobCount,
            TagClusterCount = 1,
            RoutingEntropy = 0f,
            ResourceSpan = toolCount,
            ContextUtilization = 0f,
            AvgLatencyMs = avgLatencyMs,
            AvgCostPerExecution = avgCost,
            MeasuredAt = DateTimeOffset.UtcNow
        };
    }

    private (int ToolCount, int JobCount) GetStructuralMetrics(string workflowName)
    {
        if (_manifests.TryGetValue(workflowName, out var manifest))
        {
            // Count tools across all jobs (approximate — real count would need the toolkit)
            var toolCount = manifest.Jobs.Count * 3; // Heuristic: ~3 tools per job
            return (toolCount, manifest.Jobs.Count);
        }

        // No manifest available — return minimal structural data
        return (0, 1);
    }
}
