namespace Ananke.Federation.Google.Observability;

/// <summary>
/// Seam for Agent Platform observability data — Cloud Trace traces and Cloud Monitoring metrics.
/// Allows <see cref="VertexAIRemoteCellMonitor"/> to be tested without real Google Cloud calls.
/// </summary>
internal interface IAgentObservabilityClient
{
    /// <summary>
    /// Fetches recent execution traces for <paramref name="deploymentId"/> covering
    /// the last <paramref name="lookBackMinutes"/> minutes.
    /// Returns an empty list when no traces are available.
    /// </summary>
    Task<IReadOnlyList<TraceRecord>> GetTracesAsync(
        string deploymentId,
        int lookBackMinutes,
        CancellationToken ct = default);

    /// <summary>
    /// Fetches aggregated execution metrics for <paramref name="deploymentId"/> covering
    /// the last <paramref name="lookBackMinutes"/> minutes.
    /// Returns <see langword="null"/> when no data is available.
    /// </summary>
    Task<ObservabilitySnapshot?> GetMetricsSnapshotAsync(
        string deploymentId,
        int lookBackMinutes,
        CancellationToken ct = default);
}
