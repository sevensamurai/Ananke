using Ananke.Learning;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Learning.Episodes;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

namespace Ananke.Learning.Tests;

[TestFixture]
public class MonteCarloRewardPropagatorTests
{
    private InMemoryEmbedder _embedder = null!;
    private InMemoryEmpiricalMemory _memory = null!;

    [SetUp]
    public void SetUp()
    {
        _embedder = new InMemoryEmbedder();
        _memory = new InMemoryEmpiricalMemory(_embedder, dedupThreshold: 1.0f);
    }

    /// <summary>Commits entries for an episode and returns the episode.</summary>
    private async Task<Episode> SetupEpisodeAsync(
        string id,
        int stepCount,
        float terminalReward,
        float[]? intermediateRewards = null)
    {
        var steps = new List<EpisodeStep>();

        for (var i = 0; i < stepCount; i++)
        {
            var entryId = $"{id}_entry_{i}";
            await _memory.CommitAsync(new EmpiricalEntry
            {
                Id = entryId,
                Kind = EmpiricalKind.Pattern,
                Tags = [$"step-{i}"],
                Source = "test",
                Description = SemanticDescription.FromText($"{id} unique-description-{Guid.NewGuid():N}"),
                Confidence = 0.5f,
                ObservationCount = 1,
                Evidence = [],
                FirstObserved = DateTimeOffset.UtcNow,
                LastObserved = DateTimeOffset.UtcNow,
                EpisodeId = id,
                StepIndex = i
            });

            steps.Add(new EpisodeStep
            {
                StepIndex = i,
                EntryId = entryId,
                IntermediateReward = intermediateRewards is not null ? intermediateRewards[i] : 0f
            });
        }

        return new Episode
        {
            Id = id,
            Steps = steps,
            TerminalReward = terminalReward,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            CompletedAt = DateTimeOffset.UtcNow
        };
    }

    // ── PropagateDiscountsTerminalReward ──────────────────────────

    [Test]
    public async Task PropagateDiscountsTerminalReward()
    {
        // 3-step episode, terminal reward = 1.0, γ = 0.5, no intermediate rewards
        var episode = await SetupEpisodeAsync("ep-1", stepCount: 3, terminalReward: 1.0f);
        var propagator = new MonteCarloRewardPropagator(new RewardPropagationOptions
        {
            DiscountFactor = 0.5f,
            IncludeIntermediateRewards = false
        });

        var reinforced = await propagator.PropagateAsync(episode, _memory);

        reinforced.ShouldBe(3);

        // Step 2 (terminal): return = 1.0
        var step2 = await _memory.GetAsync("ep-1_entry_2");
        step2.ShouldNotBeNull();
        step2.Evidence.ShouldContain(e => e.Contains("return:1.000"));

        // Step 1: return = γ × 1.0 = 0.5
        var step1 = await _memory.GetAsync("ep-1_entry_1");
        step1.ShouldNotBeNull();
        step1.Evidence.ShouldContain(e => e.Contains("return:0.500"));

        // Step 0: return = γ² × 1.0 = 0.25
        var step0 = await _memory.GetAsync("ep-1_entry_0");
        step0.ShouldNotBeNull();
        step0.Evidence.ShouldContain(e => e.Contains("return:0.250"));
    }

    // ── PropagateWithIntermediateRewards ──────────────────────────

    [Test]
    public async Task PropagateWithIntermediateRewards()
    {
        // 3-step episode, terminal = 1.0, intermediate = [0.1, 0.2, 0.0], γ = 0.5
        var episode = await SetupEpisodeAsync("ep-2", stepCount: 3, terminalReward: 1.0f,
            intermediateRewards: [0.1f, 0.2f, 0.0f]);
        var propagator = new MonteCarloRewardPropagator(new RewardPropagationOptions
        {
            DiscountFactor = 0.5f,
            IncludeIntermediateRewards = true
        });

        var reinforced = await propagator.PropagateAsync(episode, _memory);

        reinforced.ShouldBe(3);

        // Step 2: return = terminal + intermediate[2] = 1.0 + 0.0 = 1.0
        var step2 = await _memory.GetAsync("ep-2_entry_2");
        step2.ShouldNotBeNull();
        step2.Evidence.ShouldContain(e => e.Contains("return:1.000"));

        // Step 1: return = intermediate[1] + γ × returns[2] = 0.2 + 0.5 × 1.0 = 0.7
        var step1 = await _memory.GetAsync("ep-2_entry_1");
        step1.ShouldNotBeNull();
        step1.Evidence.ShouldContain(e => e.Contains("return:0.700"));

        // Step 0: return = intermediate[0] + γ × returns[1] = 0.1 + 0.5 × 0.7 = 0.45
        var step0 = await _memory.GetAsync("ep-2_entry_0");
        step0.ShouldNotBeNull();
        step0.Evidence.ShouldContain(e => e.Contains("return:0.450"));
    }

    // ── PropagateSkipsMissingEntries ─────────────────────────────

    [Test]
    public async Task PropagateSkipsMissingEntries()
    {
        // Create episode but only commit entries for step 0 and step 2 (step 1 missing)
        var entryId0 = "ep-3_entry_0";
        var entryId2 = "ep-3_entry_2";

        await _memory.CommitAsync(new EmpiricalEntry
        {
            Id = entryId0,
            Kind = EmpiricalKind.Pattern,
            Tags = [],
            Source = "test",
            Description = SemanticDescription.FromText($"alpha opening strategy {Guid.NewGuid():N}"),
            Confidence = 0.5f,
            ObservationCount = 1,
            Evidence = [],
            FirstObserved = DateTimeOffset.UtcNow,
            LastObserved = DateTimeOffset.UtcNow
        });

        await _memory.CommitAsync(new EmpiricalEntry
        {
            Id = entryId2,
            Kind = EmpiricalKind.Pattern,
            Tags = [],
            Source = "test",
            Description = SemanticDescription.FromText($"gamma endgame checkmate {Guid.NewGuid():N}"),
            Confidence = 0.5f,
            ObservationCount = 1,
            Evidence = [],
            FirstObserved = DateTimeOffset.UtcNow,
            LastObserved = DateTimeOffset.UtcNow
        });

        var episode = new Episode
        {
            Id = "ep-3",
            Steps =
            [
                new EpisodeStep { StepIndex = 0, EntryId = entryId0 },
                new EpisodeStep { StepIndex = 1, EntryId = "ep-3_entry_1_missing" },
                new EpisodeStep { StepIndex = 2, EntryId = entryId2 }
            ],
            TerminalReward = 1.0f,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            CompletedAt = DateTimeOffset.UtcNow
        };

        var propagator = new MonteCarloRewardPropagator(new RewardPropagationOptions
        {
            DiscountFactor = 0.9f,
            IncludeIntermediateRewards = false
        });

        var reinforced = await propagator.PropagateAsync(episode, _memory);

        // Only 2 entries exist — step 1 is skipped gracefully
        reinforced.ShouldBe(2);
    }

    // ── PropagateIdempotent ──────────────────────────────────────

    [Test]
    public async Task PropagateIdempotent()
    {
        var episode = await SetupEpisodeAsync("ep-4", stepCount: 2, terminalReward: 1.0f);
        var propagator = new MonteCarloRewardPropagator(new RewardPropagationOptions
        {
            DiscountFactor = 0.9f,
            IncludeIntermediateRewards = false
        });

        // Propagate twice
        await propagator.PropagateAsync(episode, _memory);
        await propagator.PropagateAsync(episode, _memory);

        // Entry should have 2 evidence entries (one per propagation) but not be corrupted
        var entry = await _memory.GetAsync("ep-4_entry_0");
        entry.ShouldNotBeNull();
        entry.Evidence.Count(e => e.Contains("episode:ep-4")).ShouldBe(2);
    }

    // ── DiscountFactorZeroOnlyRewardsTerminal ────────────────────

    [Test]
    public async Task DiscountFactorZeroOnlyRewardsTerminal()
    {
        var episode = await SetupEpisodeAsync("ep-5", stepCount: 3, terminalReward: 1.0f);
        var propagator = new MonteCarloRewardPropagator(new RewardPropagationOptions
        {
            DiscountFactor = 0f,
            IncludeIntermediateRewards = false
        });

        var reinforced = await propagator.PropagateAsync(episode, _memory);
        reinforced.ShouldBe(3);

        // Terminal step gets full reward
        var stepT = await _memory.GetAsync("ep-5_entry_2");
        stepT.ShouldNotBeNull();
        stepT.Evidence.ShouldContain(e => e.Contains("return:1.000"));

        // Earlier steps get zero (γ=0 means no discount propagation)
        var step1 = await _memory.GetAsync("ep-5_entry_1");
        step1.ShouldNotBeNull();
        step1.Evidence.ShouldContain(e => e.Contains("return:0.000"));

        var step0 = await _memory.GetAsync("ep-5_entry_0");
        step0.ShouldNotBeNull();
        step0.Evidence.ShouldContain(e => e.Contains("return:0.000"));
    }

    // ── DiscountFactorOneGivesEqualCredit ────────────────────────

    [Test]
    public async Task DiscountFactorOneGivesEqualCredit()
    {
        var episode = await SetupEpisodeAsync("ep-6", stepCount: 3, terminalReward: 1.0f);
        var propagator = new MonteCarloRewardPropagator(new RewardPropagationOptions
        {
            DiscountFactor = 1.0f,
            IncludeIntermediateRewards = false
        });

        var reinforced = await propagator.PropagateAsync(episode, _memory);
        reinforced.ShouldBe(3);

        // All steps get the full terminal reward (γ=1 means no discounting)
        var step0 = await _memory.GetAsync("ep-6_entry_0");
        step0.ShouldNotBeNull();
        step0.Evidence.ShouldContain(e => e.Contains("return:1.000"));

        var step1 = await _memory.GetAsync("ep-6_entry_1");
        step1.ShouldNotBeNull();
        step1.Evidence.ShouldContain(e => e.Contains("return:1.000"));

        var step2 = await _memory.GetAsync("ep-6_entry_2");
        step2.ShouldNotBeNull();
        step2.Evidence.ShouldContain(e => e.Contains("return:1.000"));
    }
}
