namespace Ananke.Learning.Exploration;

/// <summary>
/// Epsilon-greedy exploration strategy with optional annealing. With
/// probability <c>ε</c> selects a random action (explore); otherwise
/// selects the highest-scoring action (exploit).
/// </summary>
/// <remarks>
/// Annealing formula: <c>ε(t) = max(εMin, ε₀ × decay^t)</c> where
/// <c>t</c> is <c>totalSelections</c>. This gradually shifts from
/// exploration to exploitation as experience accumulates.
/// </remarks>
public sealed class EpsilonGreedyExplorationStrategy : IExplorationStrategy
{
    private readonly ExplorationOptions _options;
    private readonly Random _rng;

    /// <summary>
    /// Creates a new epsilon-greedy strategy.
    /// </summary>
    /// <param name="options">Configuration for epsilon, annealing, and bounds.</param>
    /// <param name="rng">
    /// Optional random number generator for deterministic testing.
    /// When <see langword="null"/>, a default instance is used.
    /// </param>
    public EpsilonGreedyExplorationStrategy(
        ExplorationOptions? options = null,
        Random? rng = null)
    {
        _options = options ?? new();
        _rng = rng ?? new Random();
    }

    /// <inheritdoc />
    public int SelectAction(IReadOnlyList<ActionCandidate> actions, int totalSelections)
    {
        if (actions.Count == 0) throw new ArgumentException("No actions to select from.", nameof(actions));

        var epsilon = MathF.Max(
            _options.EpsilonMin,
            _options.EpsilonInitial * MathF.Pow(_options.EpsilonDecay, totalSelections));

        if (_rng.NextSingle() < epsilon)
        {
            // Explore: random action
            return _rng.Next(actions.Count);
        }

        // Exploit: best score
        var bestIndex = 0;
        var bestScore = actions[0].Score;
        for (var i = 1; i < actions.Count; i++)
        {
            if (actions[i].Score > bestScore)
            {
                bestScore = actions[i].Score;
                bestIndex = i;
            }
        }

        return bestIndex;
    }
}
