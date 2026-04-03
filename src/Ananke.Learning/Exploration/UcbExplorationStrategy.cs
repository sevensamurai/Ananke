namespace Ananke.Learning.Exploration;

/// <summary>
/// Upper Confidence Bound (UCB1) exploration strategy. Balances exploitation
/// of high-scoring actions with exploration of under-tried or uncertain actions.
/// </summary>
/// <remarks>
/// <para>
/// UCB score for action <c>a</c>:
/// <code>UCB(a) = score(a) + c × √(ln(N) / n(a)) + w × √(variance(a))</code>
/// where <c>c</c> is the exploration coefficient, <c>N</c> is total selections,
/// <c>n(a)</c> is the selection count for action <c>a</c>, and the variance
/// bonus is optional.
/// </para>
/// <para>
/// Untried actions (<c>n(a) = 0</c>) are selected immediately — they have
/// infinite exploration bonus.
/// </para>
/// </remarks>
public sealed class UcbExplorationStrategy(
    ExplorationOptions? options = null) : IExplorationStrategy
{
    private readonly ExplorationOptions _options = options ?? new();

    /// <inheritdoc />
    public int SelectAction(IReadOnlyList<ActionCandidate> actions, int totalSelections)
    {
        if (actions.Count == 0) throw new ArgumentException("No actions to select from.", nameof(actions));

        var bestIndex = 0;
        var bestScore = float.NegativeInfinity;

        for (var i = 0; i < actions.Count; i++)
        {
            var a = actions[i];

            // Untried actions get infinite bonus
            if (a.SelectionCount == 0)
                return i;

            var explorationBonus = _options.ExplorationCoefficient
                * MathF.Sqrt(MathF.Log(totalSelections + 1) / a.SelectionCount);

            // Optionally incorporate entry variance as additional uncertainty
            var varianceBonus = _options.UseVarianceBonus
                ? _options.VarianceBonusWeight * MathF.Sqrt(a.Uncertainty)
                : 0f;

            var ucbScore = a.Score + explorationBonus + varianceBonus;

            if (ucbScore > bestScore)
            {
                bestScore = ucbScore;
                bestIndex = i;
            }
        }

        return bestIndex;
    }
}
