using Ananke.Federation.Google.Observability;
using Ananke.Federation.Monitoring;

namespace Ananke.Federation.Google;

/// <summary>
/// Collects health and execution metrics from Gemini Enterprise Agent Platform deployments
/// by querying Cloud Trace v2 (per-invocation traces) and Cloud Monitoring v3
/// (aggregated execution metrics) via Agent Observability.
/// </summary>
public sealed class VertexAIRemoteCellMonitor : IRemoteCellMonitor
{
    private readonly IAgentObservabilityClient _observabilityClient;
    private readonly RemoteCellMonitorOptions _options;

    /// <summary>
    /// Creates a monitor for production use, building an <see cref="AgentObservabilityClient"/>
    /// backed by Application Default Credentials.
    /// </summary>
    /// <param name="project">Google Cloud project ID.</param>
    /// <param name="options">Health thresholds and look-back window. Defaults are used when <see langword="null"/>.</param>
    public VertexAIRemoteCellMonitor(string project, RemoteCellMonitorOptions? options = null)
        : this(new AgentObservabilityClient(project), options) { }

    /// <summary>
    /// Creates a monitor with an explicit <see cref="IAgentObservabilityClient"/> seam for testing.
    /// </summary>
    internal VertexAIRemoteCellMonitor(
        IAgentObservabilityClient observabilityClient,
        RemoteCellMonitorOptions? options = null)
    {
        _observabilityClient = observabilityClient ?? throw new ArgumentNullException(nameof(observabilityClient));
        _options = options ?? new RemoteCellMonitorOptions();
    }

    /// <inheritdoc />
    public string Platform => AgentPlatformConstants.Platform;

    /// <inheritdoc />
    public async Task<RemoteCellHealth> GetHealthAsync(string deploymentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentId);

        try
        {
            var traces = await _observabilityClient
                .GetTracesAsync(deploymentId, _options.LookBackMinutes, ct)
                .ConfigureAwait(false);

            if (traces.Count == 0)
            {
                // No recent data — assume healthy (newly deployed or idle)
                return new RemoteCellHealth
                {
                    DeploymentId = deploymentId,
                    IsHealthy = true,
                    LastHeartbeat = DateTimeOffset.UtcNow,
                    ErrorCount = 0,
                    LatencyMs = 0
                };
            }

            var errorCount = traces.Count(t => t.IsError);
            var errorRate = (double)errorCount / traces.Count;
            var avgLatencyMs = traces.Average(t => t.LatencyMs);
            var lastActivity = traces.Max(t => t.StartTime);

            var isHealthy = errorRate <= _options.ErrorRateThreshold
                         && avgLatencyMs <= _options.LatencyThresholdMs;

            return new RemoteCellHealth
            {
                DeploymentId = deploymentId,
                IsHealthy = isHealthy,
                LastHeartbeat = lastActivity,
                ErrorCount = errorCount,
                LatencyMs = avgLatencyMs
            };
        }
        catch (Exception)
        {
            // Observability API unavailable — degrade gracefully
            return new RemoteCellHealth
            {
                DeploymentId = deploymentId,
                IsHealthy = false,
                LastHeartbeat = DateTimeOffset.UtcNow,
                ErrorCount = 0,
                LatencyMs = 0
            };
        }
    }

    /// <inheritdoc />
    public async Task<RemoteCellMetrics> GetMetricsAsync(string deploymentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentId);

        try
        {
            var snapshot = await _observabilityClient
                .GetMetricsSnapshotAsync(deploymentId, _options.LookBackMinutes, ct)
                .ConfigureAwait(false);

            if (snapshot is null)
            {
                return new RemoteCellMetrics
                {
                    DeploymentId = deploymentId,
                    ExecutionCount = 0,
                    TotalTokens = 0,
                    ToolCallCount = 0,
                    ErrorRate = 0,
                    MeasuredAt = DateTimeOffset.UtcNow
                };
            }

            return new RemoteCellMetrics
            {
                DeploymentId = deploymentId,
                ExecutionCount = snapshot.ExecutionCount,
                TotalTokens = snapshot.TotalTokens,
                ToolCallCount = snapshot.ToolCallCount,
                ErrorRate = snapshot.ErrorRate,
                MeasuredAt = DateTimeOffset.UtcNow
            };
        }
        catch (Exception)
        {
            return new RemoteCellMetrics
            {
                DeploymentId = deploymentId,
                ExecutionCount = 0,
                TotalTokens = 0,
                ToolCallCount = 0,
                ErrorRate = 0,
                MeasuredAt = DateTimeOffset.UtcNow
            };
        }
    }
}
