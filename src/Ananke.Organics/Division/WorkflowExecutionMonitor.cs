using Ananke.Organics.Healing;
using System.Collections.Concurrent;
using Ananke.Orchestration;
using Ananke.Orchestration.Workflows;

namespace Ananke.Organics.Division;

/// <summary>
/// Real <see cref="IHealthMonitor"/> implementation. Maintains a sliding
/// window of recent <see cref="WorkflowExecution{TState}"/> results per cell
/// and combines registered structural metadata with execution telemetry to
/// produce <see cref="ComplexitySnapshot"/>s and <see cref="HealthSnapshot"/>s.
/// </summary>
/// <remarks>
/// <para>
/// <b>Structural metrics</b> (<see cref="ComplexitySnapshot.ToolCount"/>,
/// <see cref="ComplexitySnapshot.TagClusterCount"/>, etc.) come from
/// <see cref="RegisterWorkflow"/> and are available immediately.
/// </para>
/// <para>
/// <b>Telemetry metrics</b> (<see cref="ComplexitySnapshot.RoutingEntropy"/>,
/// <see cref="ComplexitySnapshot.AvgLatencyMs"/>,
/// <see cref="ComplexitySnapshot.AvgCostPerExecution"/>) are computed from
/// recorded executions and require at least one <see cref="Record{TState}"/>
/// call.
/// </para>
/// </remarks>
/// <param name="windowSize">
/// Maximum number of recent executions to retain per cell. Older executions
/// are dropped. Default: 50.
/// </param>
/// <param name="classifier">
/// Optional failure classifier for distinguishing upstream errors from workflow
/// errors. Default: new <see cref="FailureClassifier"/> with built-in patterns.
/// </param>
public sealed class WorkflowExecutionMonitor(int windowSize = 50, FailureClassifier? classifier = null) : IHealthMonitor
{
    private readonly FailureClassifier _classifier = classifier ?? new FailureClassifier();
    private readonly ConcurrentDictionary<string, StructuralProfile> _profiles = new();
    private readonly ConcurrentDictionary<string, SlidingWindow> _windows = new();

    /// <summary>
    /// Registers static structural metrics for a cell. Must be called before
    /// <see cref="GetSnapshotAsync"/> can produce structural fields. Calling again
    /// for the same cell replaces the previous profile.
    /// </summary>
    /// <param name="workflowName">Workflow name (must match the <see cref="WorkflowExecution{TState}.WorkflowName"/>).</param>
    /// <param name="profile">Static structural metrics derived from manifest and tool definitions.</param>
    public void RegisterWorkflow(string workflowName, StructuralProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);
        ArgumentNullException.ThrowIfNull(profile);
        _profiles[workflowName] = profile;
    }

    /// <inheritdoc />
    public void Record<TState>(WorkflowExecution<TState> execution)
    {
        ArgumentNullException.ThrowIfNull(execution);

        var window = _windows.GetOrAdd(execution.WorkflowName, _ => new SlidingWindow(windowSize));
        window.Add(new ExecutionRecord
        {
            TotalLatencyMs = execution.History.Sum(j => j.Duration.TotalMilliseconds),
            EstimatedCost = execution.EstimatedCost,
            JobFrequencies = execution.History
                .GroupBy(j => j.JobName)
                .ToDictionary(g => g.Key, g => g.Count()),
            RecordedAt = DateTimeOffset.UtcNow,
            Failed = execution.IsFailure,
            Origin = _classifier.Classify(execution)
        });
    }

    /// <inheritdoc />
    public Task<ComplexitySnapshot> GetSnapshotAsync(string workflowName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);

        if (!_profiles.TryGetValue(workflowName, out var profile))
            throw new InvalidOperationException(
                $"Workflow '{workflowName}' has not been registered. Call RegisterWorkflow first.");

        var (avgLatency, avgCost, entropy) = _windows.TryGetValue(workflowName, out var window)
            ? window.ComputeTelemetry()
            : (0f, 0m, 0f);

        return Task.FromResult(new ComplexitySnapshot
        {
            WorkflowName = workflowName,
            ToolCount = profile.ToolCount,
            JobCount = profile.JobCount,
            TagClusterCount = profile.TagClusterCount,
            ResourceSpan = profile.ResourceSpan,
            ContextUtilization = profile.ContextUtilization,
            RoutingEntropy = entropy,
            AvgLatencyMs = avgLatency,
            AvgCostPerExecution = avgCost,
            MeasuredAt = DateTimeOffset.UtcNow
        });
    }

    /// <inheritdoc />
    public Task<HealthSnapshot?> GetHealthSnapshotAsync(string workflowName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);

        if (!_windows.TryGetValue(workflowName, out var window))
            return Task.FromResult<HealthSnapshot?>(null);

        return Task.FromResult(window.ComputeHealth(workflowName));
    }

    private sealed record ExecutionRecord
    {
        public required double TotalLatencyMs { get; init; }
        public required decimal EstimatedCost { get; init; }
        public required Dictionary<string, int> JobFrequencies { get; init; }
        public required DateTimeOffset RecordedAt { get; init; }
        public required bool Failed { get; init; }
        public required FailureOrigin Origin { get; init; }
    }

    private sealed class SlidingWindow(int maxSize)
    {
        private readonly object _lock = new();
        private readonly LinkedList<ExecutionRecord> _records = new();

        public void Add(ExecutionRecord record)
        {
            lock (_lock)
            {
                _records.AddLast(record);
                while (_records.Count > maxSize)
                    _records.RemoveFirst();
            }
        }

        public (float AvgLatencyMs, decimal AvgCost, float RoutingEntropy) ComputeTelemetry()
        {
            List<ExecutionRecord> snapshot;
            lock (_lock)
            {
                if (_records.Count == 0)
                    return (0f, 0m, 0f);
                snapshot = [.. _records];
            }

            var avgLatency = (float)snapshot.Average(r => r.TotalLatencyMs);
            var avgCost = snapshot.Average(r => r.EstimatedCost);

            // Shannon entropy of job execution frequency distribution.
            // High entropy = generalist (evenly spread across jobs).
            // Low entropy = specialist (concentrated on few jobs).
            var totalJobCalls = new Dictionary<string, int>();
            foreach (var record in snapshot)
            {
                foreach (var (job, count) in record.JobFrequencies)
                {
                    totalJobCalls.TryGetValue(job, out var existing);
                    totalJobCalls[job] = existing + count;
                }
            }

            var entropy = ComputeShannonEntropy(totalJobCalls);
            return (avgLatency, avgCost, entropy);
        }

        public HealthSnapshot? ComputeHealth(string workflowName)
        {
            List<ExecutionRecord> snapshot;
            lock (_lock)
            {
                if (_records.Count < 3)
                    return null;
                snapshot = [.. _records];
            }

            var failed = snapshot.Where(r => r.Failed).ToList();
            var errorRate = (float)failed.Count / snapshot.Count;
            var workflowErrorRate = (float)failed.Count(r => r.Origin == FailureOrigin.Workflow) / snapshot.Count;
            var upstreamErrorRate = (float)failed.Count(r => r.Origin == FailureOrigin.Upstream) / snapshot.Count;
            var mismatchRate = (float)snapshot.Count(r => r.Origin == FailureOrigin.CapabilityMismatch) / snapshot.Count;
            var latencySlope = ComputeLinearSlope(snapshot.Select(r => (float)r.TotalLatencyMs).ToList());
            var costSlope = ComputeLinearSlope(snapshot.Select(r => (float)r.EstimatedCost).ToList());

            return new HealthSnapshot
            {
                WorkflowName = workflowName,
                ErrorRate = errorRate,
                WorkflowErrorRate = workflowErrorRate,
                UpstreamErrorRate = upstreamErrorRate,
                CapabilityMismatchRate = mismatchRate,
                LatencyTrendSlope = latencySlope,
                CostTrendSlope = costSlope,
                WindowSize = snapshot.Count,
                MeasuredAt = DateTimeOffset.UtcNow
            };
        }

        /// <summary>
        /// Simple linear regression slope over ordered samples.
        /// Positive slope = metric increasing over time.
        /// </summary>
        private static float ComputeLinearSlope(List<float> values)
        {
            var n = values.Count;
            if (n < 2) return 0f;

            // Least squares: slope = (n*Σ(xy) - Σx*Σy) / (n*Σ(x²) - (Σx)²)
            float sumX = 0, sumY = 0, sumXy = 0, sumX2 = 0;
            for (var i = 0; i < n; i++)
            {
                sumX += i;
                sumY += values[i];
                sumXy += i * values[i];
                sumX2 += i * i;
            }

            var denom = n * sumX2 - sumX * sumX;
            return denom == 0 ? 0f : (n * sumXy - sumX * sumY) / denom;
        }

        private static float ComputeShannonEntropy(Dictionary<string, int> frequencies)
        {
            var total = frequencies.Values.Sum();
            if (total == 0)
                return 0f;

            var entropy = 0.0;
            foreach (var count in frequencies.Values)
            {
                if (count == 0) continue;
                var p = (double)count / total;
                entropy -= p * Math.Log2(p);
            }

            // Normalize to [0, 1] by dividing by log2(N) where N = number of distinct jobs
            var maxEntropy = Math.Log2(frequencies.Count);
            return maxEntropy > 0 ? (float)(entropy / maxEntropy) : 0f;
        }
    }
}
