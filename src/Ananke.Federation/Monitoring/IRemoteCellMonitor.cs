namespace Ananke.Federation.Monitoring;

/// <summary>
/// Collects health and execution metrics from remote cell deployments.
/// Each platform adapter provides its own implementation.
/// </summary>
public interface IRemoteCellMonitor
{
    /// <summary>Platform identifier this monitor targets.</summary>
    string Platform { get; }

    /// <summary>Gets the current health status of a remote cell.</summary>
    Task<RemoteCellHealth> GetHealthAsync(string deploymentId, CancellationToken ct = default);

    /// <summary>Gets execution metrics for a remote cell.</summary>
    Task<RemoteCellMetrics> GetMetricsAsync(string deploymentId, CancellationToken ct = default);
}
