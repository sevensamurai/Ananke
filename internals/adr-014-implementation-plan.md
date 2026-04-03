# ADR-014: Implementation Plan — Empirical Memory Skill Learning

| Field          | Value                                                              |
|----------------|---------------------------------------------------------------------|
| **Status**     | Proposed                                                            |
| **Date**       | 2025-07-27                                                          |
| **Relates to** | ADR-014 (empirical memory skill learning)                           |

---

## Phase Overview

```
Phase 1 ─ Episode Tracking + Store        ┐
Phase 2 ─ Temporal Credit Assignment       │  Each phase is independently
Phase 3 ─ Exploration Strategy             │  shippable and backward-compatible.
Phase 4 ─ Feature Importance Learning      ├  Phases 1 → 2 have a dependency;
Phase 5 ─ Richer Simulation Outcomes       │  Phase 7 depends on 1 + 4.
Phase 6 ─ Connect4 Demo Integration        │  All others are independent.
Phase 7 ─ Portable Skill Packaging         ┘
```

All new abstractions follow the project invariant: **every infrastructure
contract ships with an in-memory implementation suitable for unit testing.**

---

## Phase 1 — Episode Tracking and Store

**Goal:** Give the system a first-class concept of episodes — ordered sequences
of decisions linked by causal transitions and terminal outcomes.

### New types

| Type | Location | Purpose |
|---|---|---|
| `Episode` | `Ananke.Orchestration/Memory/EpisodeTypes.cs` | Completed episode record |
| `EpisodeStep` | `Ananke.Orchestration/Memory/EpisodeTypes.cs` | Single step in a trajectory |
| `IEpisodeStore` | `Ananke.Orchestration/Memory/IEpisodeStore.cs` | Persistence contract |
| `InMemoryEpisodeStore` | `Ananke.Orchestration/Memory/InMemoryEpisodeStore.cs` | Test/single-process implementation |

### Modified types

| Type | Change |
|---|---|
| `EmpiricalEntry` | Add optional `EpisodeId` (string?) and `StepIndex` (int?) properties |

### `Episode` record

```csharp
namespace Ananke.Orchestration.Memory;

/// <summary>
/// A completed episode — an ordered sequence of state→action transitions
/// ending in a terminal reward. Links <see cref="EmpiricalEntry"/> instances
/// into a trajectory for temporal credit assignment.
/// </summary>
public sealed record Episode
{
    public required string Id { get; init; }
    public required IReadOnlyList<EpisodeStep> Steps { get; init; }
    public required float TerminalReward { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}

/// <summary>
/// A single step in an episode trajectory.
/// </summary>
public sealed record EpisodeStep
{
    public required int StepIndex { get; init; }
    public required string EntryId { get; init; }
    public float IntermediateReward { get; init; }
}
```

### `IEpisodeStore` interface

```csharp
namespace Ananke.Orchestration.Memory;

public interface IEpisodeStore
{
    Task<Episode> CommitAsync(Episode episode, CancellationToken ct = default);
    Task<Episode?> GetAsync(string episodeId, CancellationToken ct = default);
    Task<IReadOnlyList<Episode>> BrowseAsync(
        int offset, int limit, CancellationToken ct = default);
    Task<IReadOnlyList<Episode>> BrowseByOutcomeAsync(
        float minReward, float maxReward, int offset, int limit,
        CancellationToken ct = default);
}
```

### `InMemoryEpisodeStore` implementation

```csharp
namespace Ananke.Orchestration.Memory;

/// <summary>
/// In-memory episode store for testing and single-process scenarios.
/// Episodes stored in a concurrent dictionary, browse returns reverse
/// chronological order.
/// </summary>
public sealed class InMemoryEpisodeStore : IEpisodeStore
{
    private readonly ConcurrentDictionary<string, Episode> _episodes = new();

    public Task<Episode> CommitAsync(Episode episode, CancellationToken ct)
    {
        _episodes[episode.Id] = episode;
        return Task.FromResult(episode);
    }

    public Task<Episode?> GetAsync(string episodeId, CancellationToken ct)
    {
        _episodes.TryGetValue(episodeId, out var episode);
        return Task.FromResult(episode);
    }

    public Task<IReadOnlyList<Episode>> BrowseAsync(
        int offset, int limit, CancellationToken ct)
    {
        var result = _episodes.Values
            .OrderByDescending(e => e.CompletedAt)
            .Skip(offset).Take(limit)
            .ToList();
        return Task.FromResult<IReadOnlyList<Episode>>(result);
    }

    public Task<IReadOnlyList<Episode>> BrowseByOutcomeAsync(
        float minReward, float maxReward, int offset, int limit,
        CancellationToken ct)
    {
        var result = _episodes.Values
            .Where(e => e.TerminalReward >= minReward && e.TerminalReward <= maxReward)
            .OrderByDescending(e => e.CompletedAt)
            .Skip(offset).Take(limit)
            .ToList();
        return Task.FromResult<IReadOnlyList<Episode>>(result);
    }
}
```

### `EmpiricalEntry` changes

Add two optional properties (non-breaking):

```csharp
/// <summary>
/// Episode this entry belongs to, or <see langword="null"/> for standalone entries.
/// </summary>
public string? EpisodeId { get; init; }

/// <summary>
/// Zero-based step index within the episode. Meaningful only when
/// <see cref="EpisodeId"/> is set.
/// </summary>
public int? StepIndex { get; init; }
```

### Tests

| Test | Validates |
|---|---|
| `CommitAndRetrieveEpisode` | Round-trip: commit → get by ID |
| `BrowseReturnsReverseChronological` | Ordering guarantee |
| `BrowseByOutcomeFilters` | Reward range filtering |
| `EntryWithEpisodeIdLinksToEpisode` | Entry↔Episode linkage |
| `StandaloneEntryHasNullEpisodeId` | Backward compatibility |

### Estimated effort: Small

---

## Phase 2 — Temporal Credit Assignment

**Goal:** Distribute terminal rewards backward through episode trajectories
so early decisions receive credit for outcomes they influenced.

**Depends on:** Phase 1 (episodes).

### New types

| Type | Location | Purpose |
|---|---|---|
| `IRewardPropagator` | `Ananke.Orchestration/Memory/IRewardPropagator.cs` | Credit assignment contract |
| `MonteCarloRewardPropagator` | `Ananke.Orchestration/Memory/MonteCarloRewardPropagator.cs` | Full-return discounting implementation |
| `RewardPropagationOptions` | `Ananke.Orchestration/Memory/IRewardPropagator.cs` | Configuration |

### `IRewardPropagator` interface

```csharp
namespace Ananke.Orchestration.Memory;

public interface IRewardPropagator
{
    Task<int> PropagateAsync(
        Episode episode,
        IEmpiricalMemory memory,
        CancellationToken ct = default);
}
```

### `MonteCarloRewardPropagator` implementation

The Monte Carlo return for step `t` in an episode of length `T`:

```
G(t) = Σ_{k=t}^{T} γ^(k-t) × r(k)
```

where `r(T)` is the terminal reward and `r(k)` for `k < T` is the
intermediate reward at step `k` (zero when `IncludeIntermediateRewards`
is false).

```csharp
namespace Ananke.Orchestration.Memory;

public sealed class MonteCarloRewardPropagator(
    RewardPropagationOptions? options = null) : IRewardPropagator
{
    private readonly RewardPropagationOptions _options = options ?? new();

    public async Task<int> PropagateAsync(
        Episode episode, IEmpiricalMemory memory, CancellationToken ct)
    {
        var steps = episode.Steps;
        if (steps.Count == 0) return 0;

        // Compute discounted returns backward
        var returns = new float[steps.Count];
        var T = steps.Count - 1;

        // Start from terminal step
        returns[T] = episode.TerminalReward
            + (_options.IncludeIntermediateRewards ? steps[T].IntermediateReward : 0f);

        for (var t = T - 1; t >= 0; t--)
        {
            var intermediate = _options.IncludeIntermediateRewards
                ? steps[t].IntermediateReward
                : 0f;
            returns[t] = intermediate + _options.DiscountFactor * returns[t + 1];
        }

        // Reinforce each entry with its computed return
        var reinforced = 0;
        for (var t = 0; t <= T; t++)
        {
            var entry = await memory.GetAsync(steps[t].EntryId, ct);
            if (entry is null) continue;

            await memory.ReinforceAsync(steps[t].EntryId, new Reinforcement
            {
                NewEvidence = [$"episode:{episode.Id} step:{t} return:{returns[t]:F3}"],
                Source = _options.EvidenceSource,
                Reward = returns[t]
            }, ct);
            reinforced++;
        }

        return reinforced;
    }
}
```

### `RewardPropagationOptions`

```csharp
public sealed record RewardPropagationOptions
{
    public float DiscountFactor { get; init; } = 0.95f;
    public bool IncludeIntermediateRewards { get; init; } = true;
    public string EvidenceSource { get; init; } = "reward-propagation";
}
```

### Integration with `IOfflineLearner`

The offline learner's `LearnAsync` cycle gains an optional episode propagation
step between decay and curiosity walk:

```
decay → episode propagation (new) → curiosity walk → consolidation
```

When an `IEpisodeStore` and `IRewardPropagator` are provided, the offline
learner:
1. Browses recent episodes not yet propagated
2. Calls `PropagateAsync` for each
3. Marks episodes as propagated (via metadata or a separate tracking field)

This integration is additive — when no `IEpisodeStore` is configured, the
cycle runs as before.

### Tests

| Test | Validates |
|---|---|
| `PropagateDiscountsTerminalReward` | Step 0 receives `γ^T × reward`, step T receives full reward |
| `PropagateWithIntermediateRewards` | Intermediate rewards accumulate correctly |
| `PropagateSkipsMissingEntries` | Graceful handling of deleted entries |
| `PropagateIdempotent` | Multiple propagations don't corrupt entries |
| `DiscountFactorZeroOnlyRewardsTerminal` | Edge case: γ=0 means only last step gets credit |
| `DiscountFactorOneGivesEqualCredit` | Edge case: γ=1 means all steps get full credit |

### Estimated effort: Medium

---

## Phase 3 — Exploration Strategy

**Goal:** Provide a reusable, domain-agnostic mechanism for balancing
exploitation and exploration during action selection.

**Independent of** Phases 1-2 (can ship in parallel).

### New types

| Type | Location | Purpose |
|---|---|---|
| `IExplorationStrategy` | `Ananke.Orchestration/Memory/IExplorationStrategy.cs` | Action selection contract |
| `ActionCandidate` | `Ananke.Orchestration/Memory/IExplorationStrategy.cs` | Candidate action with score and uncertainty |
| `UcbExplorationStrategy` | `Ananke.Orchestration/Memory/UcbExplorationStrategy.cs` | UCB1-based selection |
| `EpsilonGreedyExplorationStrategy` | `Ananke.Orchestration/Memory/EpsilonGreedyExplorationStrategy.cs` | ε-greedy with optional annealing |
| `ExplorationOptions` | `Ananke.Orchestration/Memory/IExplorationStrategy.cs` | Configuration |

### `IExplorationStrategy` interface

```csharp
namespace Ananke.Orchestration.Memory;

public interface IExplorationStrategy
{
    int SelectAction(IReadOnlyList<ActionCandidate> actions, int totalSelections);
}

public sealed record ActionCandidate
{
    public required float Score { get; init; }
    public required float Uncertainty { get; init; }
    public required int SelectionCount { get; init; }
}
```

### `UcbExplorationStrategy`

UCB1 variant using `EmpiricalEntry.Variance` as the uncertainty signal:

```
UCB(a) = score(a) + c × √(ln(N) / n(a))
```

where:
- `score(a)` = exploitation score (mean reward from recalled entries)
- `c` = exploration coefficient (configurable, default 1.414 = √2)
- `N` = total selections across all actions
- `n(a)` = times this action was selected

When `n(a) = 0`, the action is automatically selected (infinite exploration
bonus for untried actions).

```csharp
public sealed class UcbExplorationStrategy(
    ExplorationOptions? options = null) : IExplorationStrategy
{
    private readonly ExplorationOptions _options = options ?? new();

    public int SelectAction(IReadOnlyList<ActionCandidate> actions, int totalSelections)
    {
        if (actions.Count == 0) throw new ArgumentException("No actions to select from.");

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
```

### `EpsilonGreedyExplorationStrategy`

With optional annealing: `ε(t) = max(εMin, ε₀ × decay^t)`.

```csharp
public sealed class EpsilonGreedyExplorationStrategy(
    ExplorationOptions? options = null) : IExplorationStrategy
{
    private readonly ExplorationOptions _options = options ?? new();
    private readonly Random _rng = new();

    public int SelectAction(IReadOnlyList<ActionCandidate> actions, int totalSelections)
    {
        if (actions.Count == 0) throw new ArgumentException("No actions to select from.");

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
```

### `ExplorationOptions`

```csharp
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
```

### Tests

| Test | Validates |
|---|---|
| `UcbSelectsUntriedActionFirst` | Untried actions are always explored |
| `UcbBalancesExploitAndExplore` | High-score action chosen when well-explored; uncertain action chosen when under-explored |
| `UcbVarianceBonusFavorsUncertain` | Variance bonus increases selection probability of high-variance actions |
| `EpsilonGreedyExplorationRate` | At ε=1.0, selections are roughly uniform; at ε=0, always picks best |
| `EpsilonAnnealingDecreases` | Exploration rate decreases with `totalSelections` |
| `EmptyActionsThrows` | Both strategies reject empty input |

### Estimated effort: Small

---

## Phase 4 — Feature Importance Learning

**Goal:** Track which semantic tags correlate with positive outcomes and produce
a `TagImportanceMap` that boosts discriminating tags at recall time.

**Independent of** Phases 1-3 (can ship in parallel).

### New types

| Type | Location | Purpose |
|---|---|---|
| `TagImportanceMap` | `Ananke.Orchestration/Memory/TagImportanceMap.cs` | Learned feature weights |
| `ITagImportanceTracker` | `Ananke.Orchestration/Memory/ITagImportanceTracker.cs` | Computation contract |
| `InMemoryTagImportanceTracker` | `Ananke.Orchestration/Memory/InMemoryTagImportanceTracker.cs` | Implementation |

### Algorithm

For each tag `t` across all entries in empirical memory:

```
positive_count(t) = count of entries where tag t is present AND Valence > 0
negative_count(t) = count of entries where tag t is present AND Valence < 0
total_count(t)    = positive_count(t) + negative_count(t)

importance(t) = (positive_count(t) - negative_count(t)) / total_count(t)
                normalized to [0.0, 1.0]
```

Tags that consistently appear in positive-outcome entries get importance
approaching 1.0. Tags that appear equally in positive and negative outcomes
get importance ≈ 0.5 (neutral). Tags that only appear in negative outcomes
approach 0.0.

### Integration with recall

The `TagImportanceMap` can be applied as a **recall-time boost**:

```csharp
// In recall scoring, when TagImportanceMap is available:
var adjustedTagWeight = originalTagWeight * importanceMap.GetImportance(tagKey);
```

This does not change the `RecallAsync` signature — the map is injected into
the memory implementation or applied as a post-processing step on results.

### Integration with offline learner

The offline learner's cycle gains an optional feature importance sweep:

```
decay → episode propagation → curiosity walk → feature importance (new) → consolidation
```

The sweep runs periodically (e.g., every N cycles) and produces an updated
`TagImportanceMap`. The frequency is configurable via `OfflineLearnerOptions`.

### Tests

| Test | Validates |
|---|---|
| `PositiveTagsGetHighImportance` | Tags only in positive entries → importance near 1.0 |
| `MixedTagsGetNeutralImportance` | Tags in equal positive/negative → importance ≈ 0.5 |
| `NegativeTagsGetLowImportance` | Tags only in negative entries → importance near 0.0 |
| `UnseenTagsReturnNeutral` | Tags not in the map default to neutral (1.0) |
| `MinSampleSizeGuard` | Map not produced until sufficient entries are analyzed |

### Estimated effort: Small–Medium

---

## Phase 5 — Richer Simulation Outcomes

**Goal:** Extend `SimulationOutcome` so simulators can return trajectories,
enabling temporal credit assignment on simulated episodes.

**Independent of** Phases 1-4 but most valuable after Phase 2.

### Modified types

| Type | Change |
|---|---|
| `SimulationOutcome` | Add optional `Trajectory` property (`IReadOnlyList<EpisodeStep>?`) |
| `SimulationOutcome` | Add optional `IntermediateRewards` property (`IReadOnlyList<float>?`) |

### `SimulationOutcome` additions

```csharp
// New optional properties (non-breaking)

/// <summary>
/// Optional trajectory of states visited during simulation. When provided,
/// the offline learner can construct an <see cref="Episode"/> and perform
/// temporal credit assignment on the simulated experience.
/// </summary>
public IReadOnlyList<EpisodeStep>? Trajectory { get; init; }

/// <summary>
/// Optional intermediate rewards at each simulation step. Length matches
/// <see cref="Trajectory"/> when both are provided.
/// </summary>
public IReadOnlyList<float>? IntermediateRewards { get; init; }
```

### Offline learner integration

When the simulation source returns a `Trajectory`, the offline learner:
1. Commits each step as an `EmpiricalEntry` with `EpisodeId` and `StepIndex`
2. Constructs an `Episode` record and commits to `IEpisodeStore`
3. Calls `IRewardPropagator.PropagateAsync` on the simulated episode
4. Applies `SimulationEvidenceWeight` to the propagated rewards

### Impact on Connect4 demo

`Connect4SimulationSource` can be extended to return the sequence of moves
played during self-play games, enabling the offline learner to credit
opening moves that led to wins in simulation.

### Tests

| Test | Validates |
|---|---|
| `SimulationWithTrajectoryCreatesEpisode` | Trajectory → Episode construction |
| `SimulationWithoutTrajectoryIsBackwardCompatible` | Null trajectory = current behavior |
| `SimulatedEpisodeGetsWeightedPropagation` | Evidence weight applied to simulated credit |

### Estimated effort: Small

---

## Phase 6 — Connect4 Demo Integration

**Goal:** Update the Connect4 demo to use episodes, credit assignment,
exploration strategy, and skill packaging — providing a concrete worked
example and validation of the full learning-to-portability pipeline.

**Depends on:** Phases 1, 2, 3, 7 (all prior phases).

### Changes to `GameAnalyzer`

Replace individual move commits with episode-aware commits:

```csharp
// Current: commits each move independently
await memory.CommitAsync(new EmpiricalEntry { ... });

// New: commits each move with EpisodeId and StepIndex
var entry = await memory.CommitAsync(new EmpiricalEntry
{
    // ... existing fields ...
    EpisodeId = $"game_{gameNumber}",
    StepIndex = stepIndex
});
steps.Add(new EpisodeStep
{
    StepIndex = stepIndex,
    EntryId = entry.Id
});
```

After the game completes:

```csharp
// Commit the episode
var episode = await episodeStore.CommitAsync(new Episode
{
    Id = $"game_{gameNumber}",
    Steps = steps,
    TerminalReward = reward,
    StartedAt = gameStart,
    CompletedAt = DateTimeOffset.UtcNow,
    Metadata = new Dictionary<string, string>
    {
        ["opponent"] = "human",
        ["moves"] = board.MoveCount.ToString()
    }
});

// Propagate reward through trajectory
await rewardPropagator.PropagateAsync(episode, memory);
```

### Changes to `Connect4Agent`

Replace pure-exploitation column scoring with exploration-aware selection:

```csharp
// Current: always pick highest-scored column
var bestMoves = legal.Where(c => scores[c] >= bestScore).ToList();

// New: build ActionCandidates and use IExplorationStrategy
var candidates = legal.Select(c => new ActionCandidate
{
    Score = scores[c],
    Uncertainty = variances[c],   // from recalled entry variance
    SelectionCount = counts[c]     // how often this column was played
}).ToList();

var selected = explorationStrategy.SelectAction(candidates, totalMoves);
```

### Changes to `Trainer`

Add `IEpisodeStore`, `IRewardPropagator`, and skill packaging to the training loop:

```csharp
var episodeStore = new InMemoryEpisodeStore();
var propagator = new MonteCarloRewardPropagator(new RewardPropagationOptions
{
    DiscountFactor = 0.95f
});
var packager = new InMemorySkillPackager();
var format = new JsonSkillPackageFormat();
```

After training completes, export the learned skill:

```csharp
var skill = await packager.ExportAsync(
    new SkillExportOptions
    {
        Name = "connect4-strategy",
        Domain = "connect4",
        Version = "1.0.0",
        Description = $"Learned from {totalGames} games, win rate {winRate:P0}",
        MinStrength = 0.4f,
        MinConfidence = 0.3f,
        MinObservations = 3
    },
    memory, episodeStore, tagImportances: tagImportances);

await using var file = File.Create("connect4-skill.json");
await format.SerializeAsync(skill, file);
Console.WriteLine($"Exported skill: {skill.Entries.Count} entries, {skill.Episodes.Count} episodes");
```

Validate import into a fresh agent:

```csharp
// Load skill into a brand-new agent with no prior experience
await using var input = File.OpenRead("connect4-skill.json");
var loadedSkill = await format.DeserializeAsync(input);

var freshMemory = new InMemoryEmpiricalMemory(embedder);
var importResult = await packager.ImportAsync(loadedSkill, freshMemory,
    options: new SkillImportOptions { StrengthScale = 0.8f });

var freshAgent = new Connect4Agent(freshMemory);
// Measure fresh agent's win rate — should be close to trained agent's
```

### Validation metrics

| Metric | Measures | Expected improvement |
|---|---|---|
| Win rate vs. random (100 games) | Overall skill level | +15–25% over current (reward propagation credits opening play) |
| First-move diversity | Exploration effectiveness | More varied openings (UCB explores under-tried columns) |
| Convergence speed | Learning efficiency | Reaches plateau faster (credit assignment gives more signal per game) |
| Late-game accuracy | Endgame recognition | Similar to current (already strong via pattern matching) |
| **Imported skill win rate** | **Portability** | **Fresh agent with imported skill should achieve ≥ 80% of trained agent's win rate** |
| **Export/import round-trip** | **Fidelity** | **Zero entry loss: all quality-gated entries survive serialize → deserialize → import** |
| **Import cold-start time** | **Usability** | **Fresh agent plays first game within milliseconds of import (no retraining)** |

### Estimated effort: Medium

---

## Phase 7 — Portable Skill Packaging

**Goal:** Enable agents to export everything they learned about a domain into
a versioned, serializable artifact that another agent can import and use
immediately — without retraining.

**Depends on:** Phase 1 (episodes for trajectory export), Phase 4 (tag
importance for feature weight export). Can ship partially without Phase 4
(tag importance is optional in the package).

### The problem

All learned knowledge currently lives in runtime stores (`IEmpiricalMemory`,
`IEpisodeStore`, `IKnowledgeStore`). These are process-scoped or
infrastructure-bound. If you train a Connect4 agent over 500 games, that
knowledge dies when the process stops (in-memory) or is locked to one
database (Qdrant). There is no way to:

- Save the learned skill to a file and load it later
- Send the skill to another agent running elsewhere
- Version the skill as it improves across training runs
- Audit what the agent learned and how it was trained

### New types

| Type | Location | Purpose |
|---|---|---|
| `LearnedSkillPackage` | `Ananke.Orchestration/Memory/LearnedSkillPackage.cs` | The portable artifact |
| `TrainingManifest` | `Ananke.Orchestration/Memory/LearnedSkillPackage.cs` | Provenance and statistics |
| `ISkillPackager` | `Ananke.Orchestration/Memory/ISkillPackager.cs` | Export/import contract |
| `SkillExportOptions` | `Ananke.Orchestration/Memory/ISkillPackager.cs` | Quality gates for export |
| `SkillImportOptions` | `Ananke.Orchestration/Memory/ISkillPackager.cs` | Trust scaling and merge mode |
| `SkillImportMode` | `Ananke.Orchestration/Memory/ISkillPackager.cs` | Merge / Replace / AddOnly |
| `SkillImportResult` | `Ananke.Orchestration/Memory/ISkillPackager.cs` | Import summary |
| `InMemorySkillPackager` | `Ananke.Orchestration/Memory/InMemorySkillPackager.cs` | Implementation |
| `ISkillPackageFormat` | `Ananke.Orchestration/Memory/ISkillPackageFormat.cs` | Serialization contract |
| `JsonSkillPackageFormat` | `Ananke.Orchestration/Memory/JsonSkillPackageFormat.cs` | JSON serializer |

### `LearnedSkillPackage` record

```csharp
namespace Ananke.Orchestration.Memory;

/// <summary>
/// A portable, serializable bundle of everything an agent learned about a
/// domain. Contains curated empirical entries, episode trajectories,
/// consolidated knowledge, and learned feature weights.
/// </summary>
public sealed record LearnedSkillPackage
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Domain { get; init; }
    public required string Version { get; init; }
    public string? Description { get; init; }

    public required IReadOnlyList<EmpiricalEntry> Entries { get; init; }
    public IReadOnlyList<Episode> Episodes { get; init; } = [];
    public IReadOnlyList<KnowledgeDocument> Knowledge { get; init; } = [];
    public TagImportanceMap? TagImportances { get; init; }

    public required TrainingManifest Manifest { get; init; }
}

public sealed record TrainingManifest
{
    public required int TotalEpisodes { get; init; }
    public required int TotalEntries { get; init; }
    public float AverageReward { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public TimeSpan TrainingDuration { get; init; }
    public IReadOnlyDictionary<string, string> Statistics { get; init; }
        = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Configuration { get; init; }
        = new Dictionary<string, string>();
}
```

### `ISkillPackager` interface

```csharp
namespace Ananke.Orchestration.Memory;

public interface ISkillPackager
{
    Task<LearnedSkillPackage> ExportAsync(
        SkillExportOptions options,
        IEmpiricalMemory memory,
        IEpisodeStore? episodes = null,
        IKnowledgeStore? knowledge = null,
        TagImportanceMap? tagImportances = null,
        CancellationToken ct = default);

    Task<SkillImportResult> ImportAsync(
        LearnedSkillPackage package,
        IEmpiricalMemory memory,
        IEpisodeStore? episodes = null,
        IKnowledgeStore? knowledge = null,
        SkillImportOptions? options = null,
        CancellationToken ct = default);
}
```

### `InMemorySkillPackager` implementation

The export path:

```csharp
public async Task<LearnedSkillPackage> ExportAsync(
    SkillExportOptions options, IEmpiricalMemory memory,
    IEpisodeStore? episodes, IKnowledgeStore? knowledge,
    TagImportanceMap? tagImportances, CancellationToken ct)
{
    // 1. Browse all entries, apply quality gates
    var entries = new List<EmpiricalEntry>();
    var offset = 0;
    while (true)
    {
        var page = await memory.BrowseAsync(offset, 100, options.Kind, ct);
        if (page.Count == 0) break;

        entries.AddRange(page.Where(e =>
            e.ConsolidatedInto is null
            && e.Strength >= options.MinStrength
            && e.Confidence >= options.MinConfidence
            && e.ObservationCount >= options.MinObservations
            && (options.RequiredTags is null
                || options.RequiredTags.Any(t => e.Tags.Contains(t)))));

        offset += page.Count;
    }

    // 2. Gather episodes linked to exported entries
    var exportedEpisodes = new List<Episode>();
    if (options.IncludeEpisodes && episodes is not null)
    {
        var episodeIds = entries
            .Where(e => e.EpisodeId is not null)
            .Select(e => e.EpisodeId!)
            .Distinct();

        foreach (var id in episodeIds)
        {
            var ep = await episodes.GetAsync(id, ct);
            if (ep is not null) exportedEpisodes.Add(ep);
        }
    }

    // 3. Gather consolidated knowledge
    var knowledgeDocs = new List<KnowledgeDocument>();
    if (options.IncludeKnowledge && knowledge is not null)
    {
        // Search for domain-relevant knowledge
        var results = await knowledge.SearchAsync(options.Domain,
            new SearchOptions { TopK = 100 }, ct);
        knowledgeDocs.AddRange(results.Select(r =>
            new KnowledgeDocument { Id = r.Id, Text = r.Text, Metadata = r.Metadata }));
    }

    // 4. Package
    return new LearnedSkillPackage
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = options.Name,
        Domain = options.Domain,
        Version = options.Version,
        Description = options.Description,
        Entries = entries,
        Episodes = exportedEpisodes,
        Knowledge = knowledgeDocs,
        TagImportances = tagImportances,
        Manifest = new TrainingManifest
        {
            TotalEpisodes = exportedEpisodes.Count,
            TotalEntries = entries.Count,
            CreatedAt = DateTimeOffset.UtcNow
        }
    };
}
```

The import path:

```csharp
public async Task<SkillImportResult> ImportAsync(
    LearnedSkillPackage package, IEmpiricalMemory memory,
    IEpisodeStore? episodes, IKnowledgeStore? knowledge,
    SkillImportOptions? options, CancellationToken ct)
{
    var opts = options ?? new SkillImportOptions();
    var added = 0; var merged = 0; var skipped = 0;

    foreach (var entry in package.Entries)
    {
        // Apply trust scaling to imported entries
        var importedEntry = entry with
        {
            Id = opts.IdPrefix is not null ? $"{opts.IdPrefix}{entry.Id}" : entry.Id,
            Strength = entry.Strength * opts.StrengthScale,
            Source = opts.EvidenceSource
        };

        // CommitAsync handles dedup: if a similar entry exists, it reinforces instead
        var committed = await memory.CommitAsync(importedEntry, ct);

        if (committed.ObservationCount > importedEntry.ObservationCount)
            merged++; // dedup merged into existing
        else
            added++;
    }

    // Import episodes
    var episodesImported = 0;
    if (episodes is not null)
    {
        foreach (var episode in package.Episodes)
        {
            await episodes.CommitAsync(episode, ct);
            episodesImported++;
        }
    }

    // Import knowledge
    var knowledgeImported = 0;
    if (knowledge is not null && package.Knowledge.Count > 0)
    {
        await knowledge.UpsertAsync(package.Knowledge, ct);
        knowledgeImported = package.Knowledge.Count;
    }

    return new SkillImportResult
    {
        EntriesAdded = added,
        EntriesMerged = merged,
        EntriesSkipped = skipped,
        EpisodesImported = episodesImported,
        KnowledgeDocumentsImported = knowledgeImported
    };
}
```

### `ISkillPackageFormat` — JSON serialization

```csharp
namespace Ananke.Orchestration.Memory;

public interface ISkillPackageFormat
{
    string ContentType { get; }
    Task SerializeAsync(LearnedSkillPackage package, Stream output, CancellationToken ct = default);
    Task<LearnedSkillPackage> DeserializeAsync(Stream input, CancellationToken ct = default);
}

/// <summary>
/// JSON serializer using System.Text.Json. Produces human-readable output
/// suitable for version control, inspection, and debugging.
/// </summary>
public sealed class JsonSkillPackageFormat : ISkillPackageFormat
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ContentType => "application/json";

    public async Task SerializeAsync(
        LearnedSkillPackage package, Stream output, CancellationToken ct)
    {
        await JsonSerializer.SerializeAsync(output, package, Options, ct);
    }

    public async Task<LearnedSkillPackage> DeserializeAsync(
        Stream input, CancellationToken ct)
    {
        return await JsonSerializer.DeserializeAsync<LearnedSkillPackage>(
            input, Options, ct)
            ?? throw new InvalidOperationException("Deserialized package was null.");
    }
}
```

### End-to-end usage

```csharp
// ── After training ──────────────────────────────────
var packager = new InMemorySkillPackager();
var format = new JsonSkillPackageFormat();

var skill = await packager.ExportAsync(
    new SkillExportOptions
    {
        Name = "connect4-strategy",
        Domain = "connect4",
        Version = "1.0.0",
        Description = "Connect4 strategy learned from 500 self-play games",
        MinStrength = 0.4f,
        MinConfidence = 0.3f,
        MinObservations = 3
    },
    memory, episodeStore, knowledgeStore, tagImportances);

// Save to file
await using var file = File.Create("connect4-skill-v1.json");
await format.SerializeAsync(skill, file);

// ── Later, in a fresh agent ─────────────────────────
await using var input = File.OpenRead("connect4-skill-v1.json");
var loadedSkill = await format.DeserializeAsync(input);

var freshMemory = new InMemoryEmpiricalMemory(embedder);
var result = await packager.ImportAsync(
    loadedSkill, freshMemory,
    options: new SkillImportOptions
    {
        Mode = SkillImportMode.Merge,
        StrengthScale = 0.8f  // 80% trust in foreign knowledge
    });

// freshMemory now contains the Connect4 skill — ready to play
Console.WriteLine($"Imported: {result.EntriesAdded} new, {result.EntriesMerged} merged");
```

### Tests

| Test | Validates |
|---|---|
| `ExportFiltersbyStrength` | Entries below MinStrength are excluded |
| `ExportFiltersbyConfidence` | Entries below MinConfidence are excluded |
| `ExportFiltersbyObservations` | Entries below MinObservations are excluded |
| `ExportIncludesLinkedEpisodes` | Only episodes referenced by exported entries are included |
| `ExportOmitsConsolidatedEntries` | Entries with ConsolidatedInto are excluded (they're in Knowledge) |
| `ImportMergeDedups` | Importing into non-empty memory merges similar entries |
| `ImportAddOnlySkipsDuplicates` | AddOnly mode skips existing entries |
| `ImportAppliesStrengthScale` | Imported entries have strength × 0.8 |
| `ImportAppliesIdPrefix` | Entry IDs get the configured prefix |
| `JsonRoundTrip` | Serialize → deserialize produces identical package |
| `JsonRoundTripWithNulls` | Optional fields (Episodes, Knowledge, TagImportances) handle null |
| `EmptyMemoryExportsEmptyPackage` | Graceful handling of no matching entries |
| `ImportIntoFreshMemoryWorks` | Full pipeline: export → serialize → deserialize → import |

### Estimated effort: Medium

---

## Priority and Sequencing

```
                    ┌──────────────────────┐
                    │  Phase 1: Episodes   │ ◄── Foundation
                    └──────────┬───────────┘
                               │ depends on
                    ┌──────────▼───────────┐
                    │  Phase 2: Credit     │ ◄── Highest impact
                    │  Assignment          │
                    └──────────┬───────────┘
                               │
        ┌──────────────────────┼──────────────────────┐
        │                      │                      │
┌───────▼───────┐   ┌─────────▼─────────┐   ┌────────▼────────┐
│ Phase 3:      │   │ Phase 4: Feature  │   │ Phase 5: Richer │
│ Exploration   │   │ Importance        │   │ Simulation      │
└───────┬───────┘   └─────────┬─────────┘   └────────┬────────┘
        │                     │                       │
        │              ┌──────▼──────────┐            │
        │              │  Phase 7: Skill │            │
        │              │  Packaging      │ ◄── Portability
        │              └──────┬──────────┘            │
        │                     │                       │
        └─────────────────────┼───────────────────────┘
                              │
                    ┌─────────▼─────────┐
                    │  Phase 6: Demo    │ ◄── Validation
                    │  Integration      │
                    └───────────────────┘
```

### Recommended order

1. **Phase 1 + Phase 2** (sequential) — Episode tracking and credit assignment
   together. This is the highest-impact pair and provides the temporal
   scaffolding everything else builds on.

2. **Phase 3** — Exploration strategy. Immediately useful in the Connect4 demo
   and any action-selection context. Small, self-contained.

3. **Phase 5** — Richer simulation outcomes. Small change, high leverage when
   combined with Phase 2.

4. **Phase 4** — Feature importance. Most valuable after enough entries exist
   with diverse tags and outcomes. Can run in background.

5. **Phase 7** — Portable skill packaging. Depends on Phase 1 (episodes) and
   benefits from Phase 4 (tag importance). This is the capstone that makes
   learning a reusable asset — an agent trains once, exports the skill, and
   any number of agents import it. Ship after the learning infrastructure is
   validated.

6. **Phase 6** — Demo integration. Validates all prior phases including
   skill export/import with measurable metrics.

---

## Summary

| Phase | New types | Modified types | Effort | Impact |
|---|---|---|---|---|
| 1. Episode Tracking | `Episode`, `EpisodeStep`, `IEpisodeStore`, `InMemoryEpisodeStore` | `EmpiricalEntry` (+2 props) | Small | Foundation |
| 2. Credit Assignment | `IRewardPropagator`, `MonteCarloRewardPropagator`, `RewardPropagationOptions` | `IOfflineLearner` (optional integration) | Medium | **Highest** |
| 3. Exploration | `IExplorationStrategy`, `ActionCandidate`, `UcbExplorationStrategy`, `EpsilonGreedyExplorationStrategy`, `ExplorationOptions` | — | Small | High |
| 4. Feature Importance | `TagImportanceMap`, `ITagImportanceTracker`, `InMemoryTagImportanceTracker` | `IOfflineLearner` (optional sweep) | Small–Med | Medium |
| 5. Richer Simulation | — | `SimulationOutcome` (+2 props) | Small | Medium |
| 6. Demo Integration | — | `GameAnalyzer`, `Connect4Agent`, `Trainer` | Medium | Validation |
| 7. Skill Packaging | `LearnedSkillPackage`, `TrainingManifest`, `ISkillPackager`, `InMemorySkillPackager`, `ISkillPackageFormat`, `JsonSkillPackageFormat`, `SkillExportOptions`, `SkillImportOptions`, `SkillImportMode`, `SkillImportResult` | — | Medium | **Portability** |
