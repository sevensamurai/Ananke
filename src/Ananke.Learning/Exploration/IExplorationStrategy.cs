using Ananke.Learning.EmpiricalMemory;

namespace Ananke.Learning.Exploration;

/// <summary>
/// Domain-agnostic mechanism for balancing exploitation and exploration
/// during action selection. Given a set of candidate actions with scores
/// and uncertainty estimates, selects which action to take next.
/// </summary>
public interface IExplorationStrategy
{
    /// <summary>
    /// Selects an action index from <paramref name="actions"/>.
    /// </summary>
    /// <param name="actions">Candidate actions with scores, uncertainty, and selection counts.</param>
    /// <param name="totalSelections">Total selections across all actions so far.</param>
    /// <returns>Zero-based index into <paramref name="actions"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="actions"/> is empty.</exception>
    int SelectAction(IReadOnlyList<ActionCandidate> actions, int totalSelections);
}

/// <summary>
/// A candidate action with its exploitation score, uncertainty estimate,
/// and historical selection count.
/// </summary>
public sealed record ActionCandidate
{
    /// <summary>Exploitation score (e.g. mean reward from recalled entries).</summary>
    public required float Score { get; init; }

    /// <summary>Uncertainty estimate (e.g. variance from <see cref="EmpiricalEntry.Variance"/>).</summary>
    public required float Uncertainty { get; init; }

    /// <summary>Number of times this action has been selected previously.</summary>
    public required int SelectionCount { get; init; }
}

/// <summary>
/// Configuration shared by exploration strategy implementations.
/// </summary>
public sealed record ExplorationOptions
{
    // ── UCB ───────────────────────────────────────

    /// <summary>UCB exploration coefficient (c). Default: √2 ≈ 1.414.</summary>
    public float ExplorationCoefficient { get; init; } = 1.414f;

    /// <summary>Whether to add entry variance as additional exploration bonus.</summary>
    public bool UseVarianceBonus { get; init; } = true;

    /// <summary>Weight of the variance-derived bonus. Default: 0.5.</summary>
    public float VarianceBonusWeight { get; init; } = 0.5f;

    // ── Epsilon-greedy ───────────────────────────

    /// <summary>Initial exploration rate. Default: 0.3.</summary>
    public float EpsilonInitial { get; init; } = 0.3f;

    /// <summary>Minimum exploration rate (floor after annealing). Default: 0.05.</summary>
    public float EpsilonMin { get; init; } = 0.05f;

    /// <summary>Per-step decay factor for epsilon. Default: 0.999.</summary>
    public float EpsilonDecay { get; init; } = 0.999f;
}
