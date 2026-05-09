namespace Ananke.Federation.Monitoring;

/// <summary>
/// Health snapshot for a remote cell deployment.
/// </summary>
public sealed record RemoteCellHealth
{
    /// <summary>Deployment identifier.</summary>
    public required string DeploymentId { get; init; }

    /// <summary>Whether the remote cell is responsive.</summary>
    public required bool IsHealthy { get; init; }

    /// <summary>Last successful heartbeat or health check timestamp.</summary>
    public required DateTimeOffset LastHeartbeat { get; init; }

    /// <summary>Number of errors observed since last healthy state.</summary>
    public required int ErrorCount { get; init; }

    /// <summary>Average response latency in milliseconds.</summary>
    public required double LatencyMs { get; init; }
}
