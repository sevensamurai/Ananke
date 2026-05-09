namespace Ananke.Federation.Google.Observability;

/// <summary>
/// A single execution trace record returned by Agent Observability.
/// </summary>
internal sealed record TraceRecord
{
    /// <summary>When the invocation started.</summary>
    public required DateTimeOffset StartTime { get; init; }

    /// <summary>End-to-end latency in milliseconds for this invocation.</summary>
    public required double LatencyMs { get; init; }

    /// <summary>Whether this invocation completed with an error.</summary>
    public required bool IsError { get; init; }
}
