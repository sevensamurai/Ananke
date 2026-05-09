using Ananke.Learning;
using Ananke.Learning.EmpiricalMemory;
using Ananke.Learning.Episodes;
using Ananke.Learning.Offline;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Shouldly;

namespace Ananke.Learning.Tests;

[TestFixture]
public class SimulationOutcomeTests
{
    private InMemoryEmbedder _embedder = null!;
    private InMemoryEmpiricalMemory _memory = null!;

    [SetUp]
    public void SetUp()
    {
        _embedder = new InMemoryEmbedder();
        _memory = new InMemoryEmpiricalMemory(_embedder, dedupThreshold: 1.0f);
    }

    // ── SimulationWithTrajectoryCreatesEpisode ───────────────────

    [Test]
    public async Task SimulationWithTrajectoryCreatesEpisode()
    {
        // Simulate a 3-step trajectory returned by a simulation source
        var steps = new List<EpisodeStep>();
        for (var i = 0; i < 3; i++)
        {
            var entry = await _memory.CommitAsync(new EmpiricalEntry
            {
                Id = $"sim-entry-{i}",
                Kind = EmpiricalKind.Pattern,
                Tags = [$"sim-step-{i}"],
                Source = "simulation",
                Description = SemanticDescription.FromText($"simulated action {Guid.NewGuid():N}"),
                Confidence = 0.5f,
                ObservationCount = 1,
                Evidence = [],
                FirstObserved = DateTimeOffset.UtcNow,
                LastObserved = DateTimeOffset.UtcNow,
                EpisodeId = "sim-ep-1",
                StepIndex = i
            });

            steps.Add(new EpisodeStep
            {
                StepIndex = i,
                EntryId = entry.Id,
                IntermediateReward = i == 1 ? 0.1f : 0f
            });
        }

        var outcome = new SimulationOutcome
        {
            Reward = 0.8f,
            Summary = "Simulated 3-step scenario",
            EpisodesRun = 1,
            EpisodesSupported = 1,
            Trajectory = steps,
            IntermediateRewards = [0f, 0.1f, 0f]
        };

        // Construct an Episode from the trajectory
        var episode = new Episode
        {
            Id = "sim-ep-1",
            Steps = outcome.Trajectory!,
            TerminalReward = outcome.Reward,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, string> { ["source"] = "simulation" }
        };

        episode.Steps.Count.ShouldBe(3);
        episode.TerminalReward.ShouldBe(0.8f);
        episode.Steps[0].EntryId.ShouldBe("sim-entry-0");
        episode.Steps[1].IntermediateReward.ShouldBe(0.1f);

        // Commit to episode store
        var store = new InMemoryEpisodeStore();
        var committed = await store.CommitAsync(episode);
        committed.Id.ShouldBe("sim-ep-1");
    }

    // ── SimulationWithoutTrajectoryIsBackwardCompatible ──────────

    [Test]
    public void SimulationWithoutTrajectoryIsBackwardCompatible()
    {
        // Creating SimulationOutcome without the new properties still works
        var outcome = new SimulationOutcome
        {
            Reward = 0.5f,
            Summary = "Legacy simulation",
            EpisodesRun = 10,
            EpisodesSupported = 7
        };

        outcome.Trajectory.ShouldBeNull();
        outcome.IntermediateRewards.ShouldBeNull();
        outcome.Reward.ShouldBe(0.5f);
        outcome.EpisodesRun.ShouldBe(10);
    }

    // ── SimulatedEpisodeGetsWeightedPropagation ─────────────────

    [Test]
    public async Task SimulatedEpisodeGetsWeightedPropagation()
    {
        // Set up a 3-step simulated trajectory
        var steps = new List<EpisodeStep>();
        for (var i = 0; i < 3; i++)
        {
            await _memory.CommitAsync(new EmpiricalEntry
            {
                Id = $"weighted-entry-{i}",
                Kind = EmpiricalKind.Pattern,
                Tags = [],
                Source = "simulation",
                Description = SemanticDescription.FromText($"weighted sim {Guid.NewGuid():N}"),
                Confidence = 0.5f,
                ObservationCount = 1,
                Evidence = [],
                FirstObserved = DateTimeOffset.UtcNow,
                LastObserved = DateTimeOffset.UtcNow
            });

            steps.Add(new EpisodeStep
            {
                StepIndex = i,
                EntryId = $"weighted-entry-{i}"
            });
        }

        var outcome = new SimulationOutcome
        {
            Reward = 1.0f,
            Summary = "Full win simulation",
            EpisodesRun = 1,
            EpisodesSupported = 1,
            Trajectory = steps
        };

        // Build episode from outcome and propagate with simulation weight
        var episode = new Episode
        {
            Id = "weighted-ep",
            Steps = outcome.Trajectory!,
            TerminalReward = outcome.Reward,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt = DateTimeOffset.UtcNow
        };

        // Use a propagator with a simulation-specific evidence source
        var propagator = new MonteCarloRewardPropagator(new RewardPropagationOptions
        {
            DiscountFactor = 0.9f,
            IncludeIntermediateRewards = false,
            EvidenceSource = "simulation-propagation"
        });

        var reinforced = await propagator.PropagateAsync(episode, _memory);
        reinforced.ShouldBe(3);

        // Terminal step gets full reward
        var terminal = await _memory.GetAsync("weighted-entry-2");
        terminal.ShouldNotBeNull();
        terminal.Evidence.ShouldContain(e => e.Contains("return:1.000"));
        terminal.Evidence.ShouldContain(e => e.Contains("episode:weighted-ep"));

        // Step 0 gets discounted: γ² × 1.0 = 0.81
        var step0 = await _memory.GetAsync("weighted-entry-0");
        step0.ShouldNotBeNull();
        step0.Evidence.ShouldContain(e => e.Contains("return:0.810"));
    }
}
