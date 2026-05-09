namespace Ananke.Federation.Google.Observability;

/// <summary>
/// Aggregated execution metrics for a deployment over a recent time window,
/// returned by <see cref="IAgentObservabilityClient.GetMetricsSnapshotAsync"/>.
/// </summary>
internal sealed record ObservabilitySnapshot
{
    /// <summary>Total invocations observed in the window.</summary>
    public required long ExecutionCount { get; init; }

    /// <summary>Total tokens consumed (input + output) in the window.</summary>
    public required long TotalTokens { get; init; }

    /// <summary>Number of tool calls made in the window.</summary>
    public required long ToolCallCount { get; init; }

    /// <summary>Error rate as a fraction (0.0–1.0) in the window.</summary>
    public required double ErrorRate { get; init; }
}
