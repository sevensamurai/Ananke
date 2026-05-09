using Ananke.Learning.Exploration;
using Ananke.Organics.Division;

namespace Ananke.Organics.Sensing;

/// <summary>
/// Adaptive routing wrapper that refines an <see cref="IDomainRouter"/>'s decisions
/// over time using outcome feedback. Implements the "neural pathway formation"
/// pattern: initially trusts the division-emitted routing table,
/// then strengthens or weakens cell affinities based on observed execution outcomes.
/// </summary>
/// <remarks>
/// <para>
/// Phase 2 of Option D (Hybrid routing). Wraps any <see cref="IDomainRouter"/>
/// (typically <c>QdrantDomainRouter</c>) and layers explore/exploit on top.
/// During the exploration budget, it may override the inner router's decision
/// to test alternative cells. Once affinity scores converge, exploration
/// naturally decreases via UCB.
/// </para>
/// <para>
/// Call <see cref="RecordOutcome"/> after each routed execution to feed the
/// learning loop. Positive scores reinforce the routing choice; negative
/// scores weaken it and increase exploration.
/// </para>
/// </remarks>
/// <param name="inner">The base domain router (e.g. Qdrant-backed) that provides bootstrap routing.</param>
/// <param name="strategy">Exploration strategy (UCB) for explore/exploit balance.</param>
public sealed class RoutingAffinityTracker(
    IDomainRouter inner,
    IExplorationStrategy strategy)
    : IDomainRouter
{
    private readonly object _lock = new();
    private readonly Dictionary<string, CellAffinity> _affinities = new();
    private readonly List<string> _cellNames = [];
    private int _totalSelections;

    /// <inheritdoc />
    public async Task<string> RouteAsync(string userMessage, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);

        // Get the inner router's recommendation (Phase 1 baseline)
        var baseline = await inner.RouteAsync(userMessage, ct);

        lock (_lock)
        {
            if (_cellNames.Count < 2)
                return baseline;

            // Build action candidates from affinity scores
            var candidates = new List<ActionCandidate>(_cellNames.Count);
            foreach (var name in _cellNames)
            {
                var aff = _affinities.TryGetValue(name, out var existing) ? existing : new CellAffinity();
                candidates.Add(new ActionCandidate
                {
                    Score = aff.Selections > 0 ? aff.TotalReward / aff.Selections : 0f,
                    Uncertainty = aff.Selections > 0 ? aff.Variance : 1.0f,
                    SelectionCount = aff.Selections
                });
            }

            var selected = strategy.SelectAction(candidates, _totalSelections);
            _totalSelections++;

            return _cellNames[selected];
        }
    }

    /// <inheritdoc />
    public async Task IndexAsync(
        IReadOnlyList<ChildSpec> children,
        IReadOnlyDictionary<string, string> toolDescriptions,
        CancellationToken ct = default)
    {
        // Forward to inner router (seed Phase 1 knowledge)
        await inner.IndexAsync(children, toolDescriptions, ct);

        lock (_lock)
        {
            _cellNames.Clear();
            _affinities.Clear();
            _totalSelections = 0;

            foreach (var child in children)
            {
                _cellNames.Add(child.Name);
                _affinities[child.Name] = new CellAffinity();
            }
        }
    }

    /// <summary>
    /// Record the outcome of a routed execution. Call this after each prompt
    /// is handled to feed the adaptive learning loop.
    /// </summary>
    /// <param name="cellName">The cell that handled the prompt.</param>
    /// <param name="reward">
    /// Outcome score: positive = good routing (tools matched, low latency),
    /// negative = misroute (errors, irrelevant tools used). Range: [-1, 1].
    /// </param>
    public void RecordOutcome(string cellName, float reward)
    {
        lock (_lock)
        {
            if (!_affinities.TryGetValue(cellName, out var aff))
                return;

            aff.Selections++;
            aff.TotalReward += reward;

            // Online variance (Welford's algorithm, simplified)
            var mean = aff.TotalReward / aff.Selections;
            var delta = reward - mean;
            aff.VarianceAccumulator += delta * delta;
            aff.Variance = aff.VarianceAccumulator / aff.Selections;

            _affinities[cellName] = aff;
        }
    }

    /// <summary>
    /// Returns the current affinity scores for diagnostics.
    /// </summary>
    public IReadOnlyDictionary<string, (int Selections, float MeanReward, float Variance)> GetAffinities()
    {
        lock (_lock)
        {
            var result = new Dictionary<string, (int, float, float)>();
            foreach (var (name, aff) in _affinities)
            {
                var mean = aff.Selections > 0 ? aff.TotalReward / aff.Selections : 0f;
                result[name] = (aff.Selections, mean, aff.Variance);
            }
            return result;
        }
    }

    private record struct CellAffinity
    {
        public int Selections;
        public float TotalReward;
        public float Variance;
        public float VarianceAccumulator;
    }
}
