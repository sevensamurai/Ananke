using Ananke.Abstractions.Tools;

namespace Ananke.Orchestration.Tools.Gating;

/// <summary>
/// Tracks UCB-based affinity scores for tools. Tools that are used successfully gain
/// affinity; tools that emit fault events lose affinity and are explored less often
/// by <see cref="Routing.AffinityRerankStage"/>.
/// </summary>
/// <remarks>
/// <para>
/// Call <see cref="RecordOutcome"/> after each successful or failed tool invocation
/// to update the UCB statistics. Alternatively, register this instance as an
/// <see cref="IToolFaultObserver"/> via <c>ToolKit.WithFaultObserver</c> — fault events
/// automatically apply a negative reward.
/// </para>
/// <para>
/// Use <see cref="GetAffinities"/> at any time to snapshot the current scores for
/// diagnostics. Feed this tracker into <see cref="Routing.AffinityRerankStage"/> to
/// incorporate affinity into the smart routing pipeline.
/// </para>
/// <para>Thread-safe.</para>
/// </remarks>
public sealed class ToolAffinityTracker : IToolFaultObserver
{
    private readonly float _explorationCoefficient;
    private readonly bool _useVarianceBonus;
    private readonly float _varianceBonusWeight;
    private readonly float _faultPenalty;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, ToolAffinity> _affinities = [];
    private int _totalSelections;

    /// <summary>
    /// Creates a tracker with configurable UCB parameters.
    /// </summary>
    /// <param name="options">UCB exploration options. Uses sensible defaults when <see langword="null"/>.</param>
    /// <param name="faultPenalty">
    /// Negative reward applied when a fault event is reported for a tool.
    /// Defaults to <c>-0.5</c>. Range: (-∞, 0].
    /// </param>
    public ToolAffinityTracker(ExplorationOptions? options = null, float faultPenalty = -0.5f)
    {
        var opts = options ?? new();
        _explorationCoefficient = opts.ExplorationCoefficient;
        _useVarianceBonus = opts.UseVarianceBonus;
        _varianceBonusWeight = opts.VarianceBonusWeight;
        _faultPenalty = faultPenalty;
    }

    /// <summary>
    /// Records the outcome of a tool selection. Positive reward reinforces affinity;
    /// negative reward reduces it and increases exploration of the tool.
    /// </summary>
    /// <param name="kitName">Kit that owns the tool.</param>
    /// <param name="toolName">Name of the tool that was used.</param>
    /// <param name="reward">
    /// Outcome score in [-1, 1]. Use positive values for successful calls,
    /// negative for errors or poor results.
    /// </param>
    public void RecordOutcome(string kitName, string toolName, float reward)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        lock (_lock)
        {
            UpdateAffinity($"{kitName}::{toolName}", reward);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Applies a configurable negative reward (see <c>faultPenalty</c> constructor parameter)
    /// so faulting tools are explored less in subsequent turns.
    /// </remarks>
    public ValueTask ReportAsync(ToolFaultEvent fault, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fault);
        lock (_lock)
        {
            UpdateAffinity($"{fault.KitName}::{fault.ToolName}", _faultPenalty);
        }

        ToolMetrics.FaultReported.Add(1,
            new KeyValuePair<string, object?>("kit", fault.KitName),
            new KeyValuePair<string, object?>("tool", fault.ToolName),
            new KeyValuePair<string, object?>("contract_break", fault.ContractBreak));

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Returns a snapshot of all tracked affinity scores for diagnostics.
    /// </summary>
    public IReadOnlyDictionary<string, (int Selections, float MeanReward, float Variance)> GetAffinities()
    {
        lock (_lock)
        {
            var result = new Dictionary<string, (int, float, float)>(_affinities.Count);
            foreach (var (key, aff) in _affinities)
                result[key] = (aff.Selections, aff.Selections > 0 ? aff.TotalReward / aff.Selections : 0f, aff.Variance);
            return result;
        }
    }

    // ── UCB internals ─────────────────────────────────────────────────

    private float UcbScore(string key)
    {
        if (!_affinities.TryGetValue(key, out var aff) || aff.Selections == 0)
            return float.MaxValue; // untried → always explore first

        var mean = aff.TotalReward / aff.Selections;
        var explorationBonus = _explorationCoefficient
            * MathF.Sqrt(MathF.Log(_totalSelections + 1) / aff.Selections);
        var varianceBonus = _useVarianceBonus
            ? _varianceBonusWeight * MathF.Sqrt(aff.Variance)
            : 0f;

        return mean + explorationBonus + varianceBonus;
    }

    private void UpdateAffinity(string key, float reward)
    {
        if (!_affinities.TryGetValue(key, out var aff))
            aff = new ToolAffinity();

        aff.Selections++;
        aff.TotalReward += reward;
        _totalSelections++;

        // Welford online variance
        var mean = aff.TotalReward / aff.Selections;
        var delta = reward - mean;
        aff.VarianceAccumulator += delta * delta;
        aff.Variance = aff.VarianceAccumulator / aff.Selections;

        _affinities[key] = aff;
    }

    private struct ToolAffinity
    {
        public int Selections;
        public float TotalReward;
        public float VarianceAccumulator;
        public float Variance;
    }
}

/// <summary>
/// Exploration options for <see cref="ToolAffinityTracker"/>.
/// </summary>
public sealed record ExplorationOptions
{
    /// <summary>UCB exploration coefficient (c). Default: √2 ≈ 1.414.</summary>
    public float ExplorationCoefficient { get; init; } = 1.414f;

    /// <summary>Whether to add entry variance as an additional exploration bonus.</summary>
    public bool UseVarianceBonus { get; init; } = true;

    /// <summary>Weight of the variance-derived bonus. Default: 0.5.</summary>
    public float VarianceBonusWeight { get; init; } = 0.5f;
}
