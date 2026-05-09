namespace Ananke.Federation.Monitoring;

/// <summary>
/// Execution metrics for a remote cell deployment.
/// </summary>
public sealed record RemoteCellMetrics
{
    /// <summary>Deployment identifier.</summary>
    public required string DeploymentId { get; init; }

    /// <summary>Total number of executions since deployment.</summary>
    public required long ExecutionCount { get; init; }

    /// <summary>Total tokens consumed (input + output) since deployment.</summary>
    public required long TotalTokens { get; init; }

    /// <summary>Number of tool calls made across all executions.</summary>
    public required long ToolCallCount { get; init; }

    /// <summary>Error rate as a fraction (0.0–1.0).</summary>
    public required double ErrorRate { get; init; }

    /// <summary>When these metrics were collected.</summary>
    public required DateTimeOffset MeasuredAt { get; init; }
}
