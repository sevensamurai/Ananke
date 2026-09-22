using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ananke.Abstractions.Trajectory;
using Ananke.Orchestration.Tools;

namespace Ananke.Orchestration.Agents.Trajectory;

/// <summary>
/// Accumulates per-run tool metrics and emits a <see cref="TrajectorySnapshot"/> when
/// <see cref="CompleteAsync"/> is called. One instance per job execution.
/// </summary>
internal sealed class TrajectorySnapshotBuilder
{
    private readonly ITrajectoryObserver _observer;
    private readonly string _agentId;
    private readonly string _episodeId;
    private readonly DateTimeOffset _startedAt;

    private int _totalToolCalls;
    private int _successfulToolCalls;
    private int _hallucinatedToolCalls;
    private int _faultedToolCalls;
    private int _retryCount;
    private int _duplicateToolCalls;

    /// <summary>
    /// Fingerprints of calls already made this episode. Hashes rather than the arguments
    /// themselves — a tool call can carry anything, and this only ever needs to answer
    /// "have I seen exactly this before".
    /// </summary>
    private readonly HashSet<string> _seenCalls = new(StringComparer.Ordinal);

    public TrajectorySnapshotBuilder(
        string agentId,
        ITrajectoryObserver observer,
        string? episodeId = null)
    {
        _agentId = agentId;
        _observer = observer;
        _episodeId = episodeId ?? Guid.NewGuid().ToString("N");
        _startedAt = DateTimeOffset.UtcNow;
    }

    public string EpisodeId => _episodeId;

    internal void RecordToolCall(
        bool hallucinated, bool faulted, string? toolName = null, string? arguments = null)
    {
        if (toolName is not null)
        {
            lock (_seenCalls)
            {
                if (!_seenCalls.Add(Fingerprint(toolName, arguments)))
                    _duplicateToolCalls++;
            }
        }

        Interlocked.Increment(ref _totalToolCalls);
        if (hallucinated)
            Interlocked.Increment(ref _hallucinatedToolCalls);
        else if (faulted)
            Interlocked.Increment(ref _faultedToolCalls);
        else
            Interlocked.Increment(ref _successfulToolCalls);
    }

    internal void RecordRetry() => Interlocked.Increment(ref _retryCount);

    /// <summary>
    /// A stable, non-reversible key for one call. Arguments are re-serialized first so that
    /// whitespace and escaping differences do not read as different calls; malformed JSON is
    /// fingerprinted verbatim, since a model that emits it twice really did repeat itself.
    /// </summary>
    private static string Fingerprint(string toolName, string? arguments)
    {
        var normalized = arguments ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            try
            {
                using var parsed = JsonDocument.Parse(normalized);
                normalized = JsonSerializer.Serialize(parsed.RootElement);
            }
            catch (JsonException)
            {
                // Keep it as written.
            }
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{toolName}\u0000{normalized}"));
        return Convert.ToHexStringLower(hash);
    }

    internal async ValueTask CompleteAsync(
        bool succeeded,
        float terminalReward = 0f,
        CancellationToken ct = default)
    {
        // Classify faults: in a successful run all prior faults are "recovered";
        // in a failed run they are "abandoned".
        int recoveredFaults = 0, abandonedFaults = 0;
        if (_faultedToolCalls > 0)
        {
            if (succeeded)
            {
                recoveredFaults = _faultedToolCalls;
                ToolMetrics.FaultRecovered.Add(recoveredFaults,
                    new KeyValuePair<string, object?>("agent_id", _agentId));
            }
            else
            {
                abandonedFaults = _faultedToolCalls;
                ToolMetrics.FaultAbandoned.Add(abandonedFaults,
                    new KeyValuePair<string, object?>("agent_id", _agentId));
            }
        }

        var now = DateTimeOffset.UtcNow;
        var snapshot = new TrajectorySnapshot
        {
            EpisodeId = _episodeId,
            CapturedAt = now,
            TerminalReward = terminalReward,
            Succeeded = succeeded,
            RetryCount = _retryCount,
            TotalToolCalls = _totalToolCalls,
            SuccessfulToolCalls = _successfulToolCalls,
            HallucinatedToolCalls = _hallucinatedToolCalls,
            FaultedToolCalls = _faultedToolCalls,
            RecoveredFaults = recoveredFaults,
            AbandonedFaults = abandonedFaults,
            DuplicateToolCalls = _duplicateToolCalls,
            DistinctToolCalls = _seenCalls.Count,
            TotalCost = 0m,             // M4: wired via IBudgetMeter
            CostPerSuccessfulTrajectory = 0m,
            Duration = now - _startedAt,
        };
        await _observer.OnTrajectoryCompleteAsync(snapshot, ct).ConfigureAwait(false);
    }
}
