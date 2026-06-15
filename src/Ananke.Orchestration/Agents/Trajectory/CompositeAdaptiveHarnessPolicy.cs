using Ananke.Abstractions.Trajectory;
using Ananke.Orchestration.Tools.Gating;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ananke.Orchestration.Agents.Trajectory;

/// <summary>
/// Default adaptive harness policy that wires the existing machinery:
/// <list type="bullet">
/// <item><description>
///   Hallucinations ≥ <see cref="AdaptiveHarnessOptions.HallucinationThreshold"/>
///   → triggers a <see cref="ILearningCycleTrigger"/> learning cycle.
/// </description></item>
/// <item><description>
///   Abandoned faults in a failed trajectory
///   → applies <see cref="AdaptiveHarnessOptions.AbandonedFaultPenalty"/> to all
///   tracked tools in the configured kit via <see cref="ToolAffinityTracker"/>.
/// </description></item>
/// <item><description>
///   Successful trajectory with zero retries
///   → applies <see cref="AdaptiveHarnessOptions.SuccessReward"/> to all tracked tools.
/// </description></item>
/// </list>
/// Implements both <see cref="IAdaptiveHarnessPolicy"/> and <see cref="ITrajectoryObserver"/>
/// so it can be registered once and receive snapshots automatically from
/// <see cref="TrajectorySnapshotBuilder"/>.
/// </summary>
public sealed class CompositeAdaptiveHarnessPolicy : IAdaptiveHarnessPolicy, ITrajectoryObserver
{
    private readonly ToolAffinityTracker _tracker;
    private readonly AdaptiveHarnessOptions _options;
    private readonly ILearningCycleTrigger? _learningTrigger;
    private readonly ILogger<CompositeAdaptiveHarnessPolicy> _logger;

    /// <summary>Creates a composite policy with the given tracker and options.</summary>
    public CompositeAdaptiveHarnessPolicy(
        ToolAffinityTracker tracker,
        AdaptiveHarnessOptions? options = null,
        ILearningCycleTrigger? learningTrigger = null,
        ILogger<CompositeAdaptiveHarnessPolicy>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        _tracker = tracker;
        _options = options ?? new();
        _learningTrigger = learningTrigger;
        _logger = logger ?? NullLogger<CompositeAdaptiveHarnessPolicy>.Instance;
    }

    /// <inheritdoc />
    public async ValueTask AdaptAsync(TrajectorySnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Rule 1 — hallucinations ≥ threshold → trigger learning cycle
        if (_options.HallucinationThreshold > 0 &&
            snapshot.HallucinatedToolCalls >= _options.HallucinationThreshold &&
            _learningTrigger is not null)
        {
            _logger.LogInformation(
                "[AdaptiveHarness] Episode {EpisodeId}: {Count} hallucinations ≥ threshold {Threshold}; triggering learning cycle.",
                snapshot.EpisodeId, snapshot.HallucinatedToolCalls, _options.HallucinationThreshold);
            await _learningTrigger.TriggerAsync(ct).ConfigureAwait(false);
        }

        // Rule 2 — abandoned faults → penalise kit tools
        if (snapshot.AbandonedFaults > 0 && !string.IsNullOrEmpty(_options.KitName))
        {
            var toolsUpdated = ApplyOutcomeToKit(_options.AbandonedFaultPenalty);
            _logger.LogInformation(
                "[AdaptiveHarness] Episode {EpisodeId}: {Faults} abandoned faults; applied penalty {Penalty:F2} to {Count} tools in kit '{Kit}'.",
                snapshot.EpisodeId, snapshot.AbandonedFaults, _options.AbandonedFaultPenalty, toolsUpdated, _options.KitName);
        }

        // Rule 3 — clean success → reinforce kit tools
        if (snapshot.Succeeded && snapshot.RetryCount == 0 && !string.IsNullOrEmpty(_options.KitName))
        {
            var toolsUpdated = ApplyOutcomeToKit(_options.SuccessReward);
            _logger.LogInformation(
                "[AdaptiveHarness] Episode {EpisodeId}: succeeded with zero retries; applied reward {Reward:F2} to {Count} tools in kit '{Kit}'.",
                snapshot.EpisodeId, _options.SuccessReward, toolsUpdated, _options.KitName);
        }
    }

    /// <inheritdoc />
    public ValueTask OnTrajectoryCompleteAsync(TrajectorySnapshot snapshot, CancellationToken ct = default)
        => AdaptAsync(snapshot, ct);

    private int ApplyOutcomeToKit(float reward)
    {
        var kitPrefix = $"{_options.KitName}::";
        var toolNames = _tracker.GetAffinities().Keys
            .Where(k => k.StartsWith(kitPrefix, StringComparison.Ordinal))
            .Select(k => k[kitPrefix.Length..])
            .ToList();

        foreach (var toolName in toolNames)
            _tracker.RecordOutcome(_options.KitName, toolName, reward);

        return toolNames.Count;
    }
}
