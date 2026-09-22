using Ananke.Abstractions.Trajectory;

namespace Ananke.Orchestration.Agents.Context;

/// <summary>
/// Accumulates observations from a run and turns them into a <see cref="ContextBaseline"/>.
/// </summary>
/// <remarks>
/// <para>
/// One collector across both seams it needs — assemblies and trajectories — because the numbers only
/// make sense together: how full the window was, and how often the run asked something it had
/// already asked. Scope it with <c>ContextObserving.BeginScope</c> and register it as the trajectory
/// observer on the jobs being measured.
/// </para>
/// <para>
/// It measures; it never intervenes. Nothing here feeds back into a running workflow, which is what
/// keeps the figures a description of the system rather than a description of the collector.
/// </para>
/// <para>Safe for concurrent calls — fork branches observe in parallel.</para>
/// </remarks>
public sealed class ContextBaselineCollector : IContextObserver, ITrajectoryObserver
{
    private readonly Lock _gate = new();
    private readonly List<double> _fills = [];

    private int _calls;
    private int _windowKnown;
    private int _exceeded;
    private int _compactions;
    private int _compactionsWithheld;
    private int _callsWithContract;
    private long _contractTokens;
    private int _episodes;
    private int _toolCalls;
    private int _duplicateToolCalls;

    /// <inheritdoc />
    public ValueTask OnContextAssembledAsync(
        ContextObservation observation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(observation);

        lock (_gate)
        {
            _calls++;

            // Counted here rather than on compaction: the contract is in every assembly, and a run
            // that never compacts still pinned one on every call it made.
            if (observation.ContractTokens > 0)
            {
                _callsWithContract++;
                _contractTokens += observation.ContractTokens;
            }

            if (!observation.WindowKnown)
                return ValueTask.CompletedTask;

            _windowKnown++;
            if (observation.ExceededWindow)
                _exceeded++;

            // `HeadroomRatio` is assembled ÷ window — how *full* the window was, despite the name,
            // which follows the design's own vocabulary rather than the usual sense of headroom.
            if (observation.HeadroomRatio is { } fill)
                _fills.Add(fill);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask OnContextCompactedAsync(
        ContextCompactionRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (_gate)
        {
            _compactions++;
            if (record.Projection.Compacted)
                _compactionsWithheld++;
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask OnTrajectoryCompleteAsync(
        TrajectorySnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_gate)
        {
            _episodes++;
            _toolCalls += snapshot.TotalToolCalls;
            _duplicateToolCalls += snapshot.DuplicateToolCalls;
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Computes the baseline from everything observed so far.</summary>
    public ContextBaseline Snapshot()
    {
        lock (_gate)
        {
            var fills = _fills.Order().ToList();

            return new ContextBaseline
            {
                Calls = _calls,
                CallsWithKnownWindow = _windowKnown,
                TruncationIncidence = _windowKnown == 0 ? 0 : _exceeded / (double)_windowKnown,
                FillP95 = Percentile(fills, 0.95),
                FillP99 = Percentile(fills, 0.99),
                Episodes = _episodes,
                ToolCalls = _toolCalls,
                ReworkRate = _toolCalls == 0 ? 0 : _duplicateToolCalls / (double)_toolCalls,
                Compactions = _compactions,
                CompactionRate = _compactions == 0 ? 0 : _compactionsWithheld / (double)_compactions,
                CallsWithContract = _callsWithContract,
                MeanContractTokens = _callsWithContract == 0
                    ? 0
                    : _contractTokens / (double)_callsWithContract
            };
        }
    }

    /// <summary>
    /// Nearest-rank percentile over a sorted list. No interpolation: with a handful of samples an
    /// interpolated p99 invents a value that never occurred, and the whole point of looking at the
    /// tail is to see calls that really happened.
    /// </summary>
    private static double Percentile(IReadOnlyList<double> sorted, double q)
    {
        if (sorted.Count == 0)
            return 0;

        var rank = (int)Math.Ceiling(q * sorted.Count);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Count - 1)];
    }
}
