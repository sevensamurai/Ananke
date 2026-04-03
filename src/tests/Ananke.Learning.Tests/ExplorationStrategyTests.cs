using Ananke.Learning;
using Ananke.Learning.Exploration;
using Shouldly;

namespace Ananke.Learning.Tests;

[TestFixture]
public class ExplorationStrategyTests
{
    // ── UCB ──────────────────────────────────────────────────────

    [Test]
    public void UcbSelectsUntriedActionFirst()
    {
        var strategy = new UcbExplorationStrategy();
        var actions = new List<ActionCandidate>
        {
            new() { Score = 0.9f, Uncertainty = 0.1f, SelectionCount = 10 },
            new() { Score = 0.1f, Uncertainty = 0.1f, SelectionCount = 0 },
            new() { Score = 0.8f, Uncertainty = 0.1f, SelectionCount = 5 }
        };

        var selected = strategy.SelectAction(actions, totalSelections: 15);

        // Untried action (index 1) should always be selected
        selected.ShouldBe(1);
    }

    [Test]
    public void UcbBalancesExploitAndExplore()
    {
        var strategy = new UcbExplorationStrategy(new ExplorationOptions
        {
            ExplorationCoefficient = 1.414f,
            UseVarianceBonus = false
        });

        // When well-explored, the high-scoring action wins
        var wellExplored = new List<ActionCandidate>
        {
            new() { Score = 0.9f, Uncertainty = 0.1f, SelectionCount = 100 },
            new() { Score = 0.5f, Uncertainty = 0.1f, SelectionCount = 100 }
        };
        strategy.SelectAction(wellExplored, totalSelections: 200).ShouldBe(0);

        // When one action is under-explored, exploration bonus may override lower score
        var underExplored = new List<ActionCandidate>
        {
            new() { Score = 0.9f, Uncertainty = 0.1f, SelectionCount = 1000 },
            new() { Score = 0.5f, Uncertainty = 0.1f, SelectionCount = 1 }
        };
        strategy.SelectAction(underExplored, totalSelections: 1001).ShouldBe(1);
    }

    [Test]
    public void UcbVarianceBonusFavorsUncertain()
    {
        // Two actions with identical scores and selection counts,
        // but different uncertainty — higher uncertainty should win
        var withVariance = new UcbExplorationStrategy(new ExplorationOptions
        {
            ExplorationCoefficient = 0f, // disable UCB exploration bonus
            UseVarianceBonus = true,
            VarianceBonusWeight = 1.0f
        });

        var actions = new List<ActionCandidate>
        {
            new() { Score = 0.5f, Uncertainty = 0.01f, SelectionCount = 10 },
            new() { Score = 0.5f, Uncertainty = 1.0f, SelectionCount = 10 }
        };

        var selected = withVariance.SelectAction(actions, totalSelections: 20);
        selected.ShouldBe(1); // Higher uncertainty wins

        // With variance bonus disabled, both are equal — first wins by tie-breaking
        var withoutVariance = new UcbExplorationStrategy(new ExplorationOptions
        {
            ExplorationCoefficient = 0f,
            UseVarianceBonus = false
        });

        var selected2 = withoutVariance.SelectAction(actions, totalSelections: 20);
        selected2.ShouldBe(0); // Equal scores, first wins
    }

    // ── Epsilon-greedy ──────────────────────────────────────────

    [Test]
    public void EpsilonGreedyExplorationRate()
    {
        // At ε=1.0, always explore — selections should be roughly uniform
        var alwaysExplore = new EpsilonGreedyExplorationStrategy(
            new ExplorationOptions { EpsilonInitial = 1.0f, EpsilonMin = 1.0f },
            rng: new Random(42));

        var actions = new List<ActionCandidate>
        {
            new() { Score = 1.0f, Uncertainty = 0f, SelectionCount = 100 },
            new() { Score = 0.0f, Uncertainty = 0f, SelectionCount = 100 },
            new() { Score = 0.0f, Uncertainty = 0f, SelectionCount = 100 }
        };

        var counts = new int[3];
        for (var i = 0; i < 3000; i++)
            counts[alwaysExplore.SelectAction(actions, totalSelections: 0)]++;

        // Each action should get roughly 1000 selections (±300 for randomness)
        foreach (var count in counts)
            count.ShouldBeInRange(700, 1300);

        // At ε=0, always exploit — should always pick best score (index 0)
        var neverExplore = new EpsilonGreedyExplorationStrategy(
            new ExplorationOptions { EpsilonInitial = 0f, EpsilonMin = 0f },
            rng: new Random(42));

        for (var i = 0; i < 100; i++)
            neverExplore.SelectAction(actions, totalSelections: 0).ShouldBe(0);
    }

    [Test]
    public void EpsilonAnnealingDecreases()
    {
        // With decay, exploration rate should decrease with totalSelections.
        // At high totalSelections, the strategy should almost always exploit.
        var strategy = new EpsilonGreedyExplorationStrategy(
            new ExplorationOptions
            {
                EpsilonInitial = 1.0f,
                EpsilonMin = 0.0f,
                EpsilonDecay = 0.99f
            },
            rng: new Random(42));

        var actions = new List<ActionCandidate>
        {
            new() { Score = 1.0f, Uncertainty = 0f, SelectionCount = 50 },
            new() { Score = 0.0f, Uncertainty = 0f, SelectionCount = 50 }
        };

        // After 1000 steps: ε = 1.0 × 0.99^1000 ≈ 0.000043 → nearly always exploit
        var exploitCount = 0;
        for (var i = 0; i < 1000; i++)
        {
            if (strategy.SelectAction(actions, totalSelections: 1000) == 0)
                exploitCount++;
        }

        // Should almost always pick the best action
        exploitCount.ShouldBeGreaterThan(990);
    }

    // ── Shared edge case ────────────────────────────────────────

    [Test]
    public void EmptyActionsThrows()
    {
        var ucb = new UcbExplorationStrategy();
        var epsilon = new EpsilonGreedyExplorationStrategy();
        var empty = new List<ActionCandidate>();

        Should.Throw<ArgumentException>(() => ucb.SelectAction(empty, 0));
        Should.Throw<ArgumentException>(() => epsilon.SelectAction(empty, 0));
    }
}
