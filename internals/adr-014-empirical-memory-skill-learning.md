# ADR-014 — Empirical Memory Skill Learning: Episodes, Credit Assignment, and Policy Abstraction

| Field          | Value                                                                                   |
|----------------|-----------------------------------------------------------------------------------------|
| **Status**     | Proposed                                                                                |
| **Date**       | 2025-07-27                                                                              |
| **Authors**    | —                                                                                       |
| **Deciders**   | Ananke maintainers                                                                      |
| **Tags**       | empirical-memory, skill-learning, episodes, credit-assignment, exploration, policy, skill-packaging, portability |
| **Relates to** | ADR-007 (background cognitive processes), ADR-008 (affective signals), `IEmpiricalMemory`, `IOfflineLearner`, `ISimulationSource`, `EmpiricalEntry`, Connect4 demo |

---

## Context

ADR-007 established `IEmpiricalMemory` as a mutable store for patterns, skills,
and heuristics learned from agent collaboration. ADR-008 added prediction-error
reinforcement, affective signals, and decay. The Connect4 demo validated this
end-to-end: an agent learns board patterns through self-play and offline
consolidation.

A detailed analysis of the empirical memory infrastructure (see
`adr-014-learning-improvements.md`) identifies what works well and what is
missing to evolve from a **belief management system** to a **skill learning
system**. The core gap is the **sequential decision-making layer**: the
infrastructure can store and reinforce individual observations, but cannot
represent the trajectory of decisions that produced an outcome, nor distribute
credit across that trajectory.

### What exists (solid)

| Capability | Mechanism |
|---|---|
| Observation → Commit | `IEmpiricalMemory.CommitAsync` |
| Similarity recall (vector + tag overlap) | `RecallAsync` + `SemanticDescription.TagOverlap` |
| Prediction-error reinforcement | `Reinforcement.Reward` → PE → variance → confidence |
| Offline learning (decay, curiosity, simulation) | `IOfflineLearner`, `ISimulationSource` |
| Consolidation (empirical → knowledge) | `MarkConsolidatedAsync` |
| Affect signals for recall priority | `Valence`, `Intensity` on `EmpiricalEntry` |
| Three-kind taxonomy | `EmpiricalKind.Pattern`, `Skill`, `Heuristic` |

### What is missing (identified gaps)

| Gap | Impact |
|---|---|
| **No episode/trajectory tracking** | Cannot link a sequence of decisions to a terminal outcome. Each move is committed independently; the agent cannot learn that "move A at turn 3 caused the winning position at turn 15." |
| **No temporal credit assignment** | When a game ends, only final-state matches get reinforced. Early-game moves that set up the win receive no credit. `EmpiricalEntry.Latency` exists but is unused for multi-step propagation. |
| **No learned feature importance** | `BoardFeatures.Decompose()` is hand-crafted. The system has no mechanism to learn which `SemanticTags` dimensions correlate with positive outcomes over time. |
| **No exploration during action selection** | The offline learner has ε-greedy exploration, but during play the Connect4 agent always picks the highest-scored column — pure exploitation. `Variance` is a natural exploration bonus but isn't used at decision time. |
| **No policy abstraction** | `EmpiricalKind.Skill` has `Steps`, `Goal`, `Applicability` — but these are static text. There is no mechanism for a skill to be a function of current state: a policy that maps observations to actions and improves with experience. |
| **Simulation returns scalars only** | `SimulationOutcome` returns `Reward` and `Summary`. For skill learning, it should also return the trajectory of states visited and intermediate rewards. |
| **No portable skill packaging** | All learned knowledge lives in runtime stores. There is no way to export what an agent learned into a versioned, serializable artifact that another agent can import and use without retraining. |

### What would be nice (future extensions)

| Extension | Notes |
|---|---|
| Opponent modeling / multi-agent awareness | Learn from opponent perspective; counter-strategy synthesis |
| Abstract rule synthesis during consolidation | Merge related entries into generalized rules (e.g., "center control wins") |
| Transfer learning between domains | Reuse policies/features across different tasks |
| Skill catalog registration | Register learned skill packages in `ISkillCatalog` (ADR-011) alongside external tools |

These extensions are out of scope for this ADR but the design should not
preclude them.

---

## Analysis

### Gap 1: Episode Tracking

The fundamental unit of skill learning is the **episode** — a sequence of
state→action→next-state transitions ending in a terminal reward. Without it,
the system treats each observation as independent, losing the temporal
structure that makes sequential decision-making learnable.

The infrastructure already has the building blocks:
- `EmpiricalEntry.Tags` carries `game_N` and `move_N` tags in the Connect4 demo
- `EmpiricalEntry.Latency` exists for temporal relationships
- `CommitAsync` returns the stored entry with its ID

What's missing is a **first-class Episode record** that links entries into an
ordered trajectory and carries the terminal reward.

```
Episode "game_42"
  ├── Step 0: entry_a  (state: opening, action: col_3, reward: 0)
  ├── Step 1: entry_b  (state: mid-game, action: col_4, reward: 0)
  ├── Step 2: entry_c  (state: late-game, action: col_3, reward: 0)
  └── Terminal reward: +1.0 (agent won)
```

### Gap 2: Temporal Credit Assignment

This is the **single most impactful addition** for game learning. Without it,
the system can memorize "this final position wins" but cannot learn "this
opening leads to winning positions."

After an episode completes, a backward pass distributes discounted credit:

```
R(t) = terminal_reward × γ^(T - t)
```

where `γ` is a discount factor (e.g., 0.95) and `T - t` is the distance from
step `t` to the terminal state. Early moves receive less credit than late moves,
but still receive *some* credit — unlike the current system where they receive
*none*.

This is analogous to Monte Carlo return estimation. The existing
`Reinforcement.Reward` field already accepts the reward signal; the missing
piece is the loop that walks the trajectory backward and calls
`ReinforceAsync` with discounted rewards.

### Gap 3: Exploration During Action Selection

The `Variance` field on `EmpiricalEntry` is already a natural exploration
bonus — high-variance entries are uncertain and worth investigating. Using it
as a UCB-style exploration bonus during action selection:

```
score(action) = exploitation(action) + c × √(variance(action))
```

This balances tried-and-true moves against under-explored ones. The
exploration coefficient `c` should be configurable and decay over time as
the agent gains experience (exploration annealing).

### Gap 4: Feature Importance Learning

Currently all `SemanticTags` are treated as equally meaningful modulo their
initial weights. A background process could track tag→outcome correlations:

```
For each tag t across all reinforced entries:
  importance(t) = correlation(presence_of_t, positive_reward)
```

Tags that consistently appear in high-confidence, positively-valenced entries
should get boosted recall weight. This makes recall progressively more
discriminating without changing the `SemanticDescription` contract.

### Gap 5: Policy Abstraction

The Connect4 demo hardcodes the policy logic in `ChooseMoveAsync` — score
columns by recalled experience. This works but cannot itself be learned or
improved. A **policy** abstraction would:

1. Given current observation, query memory for similar states
2. Weight recalled actions by historical reward (exploitation)
3. Add exploration bonus from variance (exploration)
4. Return the selected action and reasoning

This is a reusable pattern across domains, not specific to Connect4.

### Gap 6: Richer Simulation Outcomes

`SimulationOutcome` currently returns a scalar `Reward` and a `Summary`. For
trajectory-aware learning, the simulator should also return:

- The sequence of states visited (for credit assignment)
- Which hypothesis-derived decisions were made (for attribution)
- Intermediate rewards (not just terminal)

This can be modeled as an optional `Trajectory` property on `SimulationOutcome`
that reuses the same `EpisodeStep` type from Gap 1.

### Gap 7: No Portable Skill Packaging

All the learning infrastructure (entries, episodes, tag importance, consolidated
knowledge) lives in runtime stores — `IEmpiricalMemory`, `IEpisodeStore`,
`IKnowledgeStore`. There is no concept of taking what an agent learned about a
domain and packaging it into a **portable artifact** that can be:

- **Exported** — serialized to a file, stream, or registry
- **Imported** — loaded into a fresh agent with no prior experience
- **Versioned** — tracked as the skill improves over training iterations
- **Shared** — distributed to other agents or stored in a skill library

This is the gap between "the agent learned something" and "the agent's
knowledge is a reusable asset."

The existing `ISkillCatalog` (ADR-011) discovers **external tools** — CLI
binaries, MCP servers, A2A agents. It handles the question "what tools exist
in the world?" A learned skill is fundamentally different: it's an **internal
artifact produced by the learning pipeline**, not an external tool to invoke.
The bridge between the two is a future concern (registering learned skills in
a catalog), but the packaging layer itself is the immediate need.

What a learned skill package would contain:

```
┌─────────────────────────────────────────────┐
│           LearnedSkillPackage                │
│                                             │
│  ┌─────────────────────────────────────┐    │
│  │  Empirical Entries                  │    │  Selected patterns, heuristics, skills
│  │  (filtered by strength/confidence)  │    │  above quality thresholds
│  ├─────────────────────────────────────┤    │
│  │  Episodes                           │    │  Trajectory history that produced
│  │  (training context)                 │    │  the learned knowledge
│  ├─────────────────────────────────────┤    │
│  │  Consolidated Knowledge             │    │  KnowledgeDocuments promoted from
│  │  (semantic layer)                   │    │  mature entries
│  ├─────────────────────────────────────┤    │
│  │  Tag Importance Map                 │    │  Learned feature weights for
│  │  (feature weights)                  │    │  discriminating recall
│  ├─────────────────────────────────────┤    │
│  │  Training Manifest                  │    │  Provenance, config, statistics,
│  │  (provenance)                       │    │  version, domain
│  └─────────────────────────────────────┘    │
└─────────────────────────────────────────────┘
```

Empiricial memory alone is not enough because:

1. **A learned skill is more than individual entries.** It includes the
   *structure* connecting them — episodes link entries into trajectories,
   tag importance maps capture which features matter, consolidated knowledge
   distills stable beliefs. The package must preserve these relationships.

2. **Memory is a runtime store; a skill is a versioned artifact.** You cannot
   serialize `InMemoryEmpiricalMemory` and call it a skill. The entries need
   to be curated (only the relevant, quality-gated ones), contextualized
   (what domain, what configuration produced them), and stripped of runtime
   state (recency timestamps become meaningless in a new context).

3. **Import needs context adaptation.** If agent A learned Connect4 patterns
   using a specific feature extraction (`BoardFeatures.Decompose`), agent B
   needs compatible feature extraction to make sense of the recalled entries.
   The manifest should declare domain and configuration dependencies.

4. **Consolidated knowledge is part of the skill but not all of it.** Some
   high-quality patterns may not have reached consolidation thresholds yet.
   The export should include entries above configurable quality gates
   (min strength, min confidence, min observations) — a broader selection
   than consolidation alone.

5. **Import should be non-destructive and tunable.** Loading a skill into
   fresh memory should support merge (combine with existing knowledge),
   replace (overwrite), or add-only (no dedup). Imported entries should
   carry a trust discount (strength scaling) since the importing agent
   didn't produce the evidence firsthand.

---

## Decision

**Introduce episode tracking, temporal credit assignment, and exploration
strategy as new abstractions layered on top of the existing empirical memory
infrastructure.** The existing `IEmpiricalMemory`, `EmpiricalEntry`,
`IOfflineLearner`, and `ISimulationSource` contracts remain unchanged. All
additions are new types and optional extensions.

### What we adopt

1. **Episode tracking** — A new `Episode` record that links `EmpiricalEntry`
   instances into ordered trajectories with terminal rewards. Episodes are
   stored alongside entries and used by credit assignment and consolidation.

2. **Temporal credit assignment** — An `IRewardPropagator` interface that
   walks completed episodes backward and reinforces each step with discounted
   reward. Ships with a `MonteCarloRewardPropagator` (full-return discounting)
   implementation.

3. **Exploration strategy** — An `IExplorationStrategy` interface for
   action-selection that balances exploitation and exploration. Ships with
   `UcbExplorationStrategy` (variance-based UCB) and
   `EpsilonGreedyExplorationStrategy` implementations.

4. **Richer simulation outcomes** — Extend `SimulationOutcome` with an
   optional trajectory so the offline learner can perform credit assignment
   on simulated episodes.

5. **Feature importance tracking** — A background process in the offline
   learner that tracks tag→outcome correlations and produces a
   `TagImportanceMap` usable at recall time.

6. **Portable skill packaging** — A `LearnedSkillPackage` record that bundles
   empirical entries, episodes, consolidated knowledge, and tag importance
   into a serializable artifact. An `ISkillPackager` interface handles
   export (with quality filtering) and import (with trust scaling and merge
   modes). Ships with a JSON-based `ISkillPackageFormat` serializer.

### What we defer

1. **Policy abstraction** — A full `IPolicy<TState, TAction>` interface is
   desirable but requires resolving the generic state/action representation
   across domains. Defer until episode tracking validates the trajectory
   model. The exploration strategy provides the immediate practical benefit.

2. **Opponent modeling** — Multi-agent awareness requires perspective-tagged
   entries and counter-strategy synthesis. Valuable for games but not
   essential for the general skill-learning infrastructure.

3. **Abstract rule synthesis** — Merging related entries during consolidation
   into generalized rules. Architecturally enabled by episodes (you can
   identify clusters of trajectories that share structure) but requires
   LLM-based summarization. Defer to a future ADR.

4. **Skill catalog registration** — Registering learned skill packages in
   `ISkillCatalog` (ADR-011) so other agents discover them alongside
   external tools. The bridge between "learned internally" and "discoverable
   externally" is a natural evolution but requires the packaging layer first.

### Design constraints

- **No breaking changes** to `IEmpiricalMemory`, `EmpiricalEntry`,
  `IOfflineLearner`, or `ISimulationSource`. All additions are new types or
  optional properties.
- **Every new interface ships with an in-memory implementation** suitable for
  unit testing (project invariant).
- **Domain-agnostic** — the episode/credit/exploration abstractions must work
  for Connect4, chess, incident investigation, or any sequential decision task.
- **Opt-in complexity** — agents that don't need trajectories continue to
  work unchanged. Episode tracking is used only when `EpisodeId` and
  `StepIndex` are populated on committed entries.
- **Serialization-friendly** — all package types must be round-trippable
  through JSON (or any `ISkillPackageFormat`). No runtime-only state in
  exported artifacts.

---

## Proposed Changes

### New types in `Ananke.Orchestration.Memory`

#### `Episode` — trajectory record

```csharp
/// <summary>
/// A completed episode — an ordered sequence of state→action transitions
/// ending in a terminal reward. Links <see cref="EmpiricalEntry"/> instances
/// into a trajectory for temporal credit assignment.
/// </summary>
public sealed record Episode
{
    /// <summary>Unique episode identifier (e.g., "game_42", "investigation_7").</summary>
    public required string Id { get; init; }

    /// <summary>Ordered steps in the trajectory, from first action to terminal state.</summary>
    public required IReadOnlyList<EpisodeStep> Steps { get; init; }

    /// <summary>Terminal reward for the episode (+1 win, -1 loss, 0 draw, etc.).</summary>
    public required float TerminalReward { get; init; }

    /// <summary>When the episode started.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>When the episode completed.</summary>
    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>Domain-specific metadata (e.g., "opponent:random", "difficulty:hard").</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}

/// <summary>
/// A single step in an episode trajectory, linking an empirical entry to
/// its position in the sequence.
/// </summary>
public sealed record EpisodeStep
{
    /// <summary>Zero-based position in the episode.</summary>
    public required int StepIndex { get; init; }

    /// <summary>ID of the <see cref="EmpiricalEntry"/> committed for this step.</summary>
    public required string EntryId { get; init; }

    /// <summary>
    /// Intermediate reward at this step (0 for most steps; non-zero for
    /// sub-goals or penalties). Distinct from the episode's terminal reward.
    /// </summary>
    public float IntermediateReward { get; init; }
}
```

#### `EmpiricalEntry` — new optional episode fields

```csharp
// Additive, non-breaking fields on EmpiricalEntry

/// <summary>
/// Episode this entry belongs to, or <see langword="null"/> for standalone
/// entries. Set at commit time when the entry is part of a trajectory.
/// </summary>
public string? EpisodeId { get; init; }

/// <summary>
/// Zero-based step index within the episode. Meaningful only when
/// <see cref="EpisodeId"/> is set.
/// </summary>
public int? StepIndex { get; init; }
```

#### `IEpisodeStore` — episode persistence

```csharp
/// <summary>
/// Persistent store for completed episodes. Used by reward propagation,
/// offline analysis, and consolidation. Implementations may co-locate
/// with <see cref="IEmpiricalMemory"/> or use a separate store.
/// </summary>
public interface IEpisodeStore
{
    /// <summary>Records a completed episode.</summary>
    Task<Episode> CommitAsync(Episode episode, CancellationToken ct = default);

    /// <summary>Retrieves an episode by ID, or <see langword="null"/> if not found.</summary>
    Task<Episode?> GetAsync(string episodeId, CancellationToken ct = default);

    /// <summary>Lists episodes in reverse chronological order.</summary>
    Task<IReadOnlyList<Episode>> BrowseAsync(
        int offset, int limit, CancellationToken ct = default);

    /// <summary>Lists episodes whose terminal reward matches a filter.</summary>
    Task<IReadOnlyList<Episode>> BrowseByOutcomeAsync(
        float minReward, float maxReward, int offset, int limit,
        CancellationToken ct = default);
}
```

#### `IRewardPropagator` — temporal credit assignment

```csharp
/// <summary>
/// Distributes a terminal reward backward through an episode's trajectory,
/// reinforcing each step with discounted credit. This is the temporal
/// credit assignment mechanism that bridges individual observations and
/// sequential outcomes.
/// </summary>
public interface IRewardPropagator
{
    /// <summary>
    /// Propagates the episode's terminal reward to each step's empirical entry.
    /// Returns the number of entries reinforced.
    /// </summary>
    Task<int> PropagateAsync(
        Episode episode,
        IEmpiricalMemory memory,
        CancellationToken ct = default);
}

/// <summary>Configuration for reward propagation.</summary>
public sealed record RewardPropagationOptions
{
    /// <summary>
    /// Discount factor per step (γ). Determines how much credit early
    /// steps receive relative to later steps. Default: 0.95.
    /// <c>R(t) = terminal_reward × γ^(T - t)</c>
    /// </summary>
    public float DiscountFactor { get; init; } = 0.95f;

    /// <summary>
    /// Whether to include intermediate rewards in the return computation.
    /// When true: <c>R(t) = Σ_{k=t}^{T} γ^(k-t) × r(k)</c>.
    /// Default: true.
    /// </summary>
    public bool IncludeIntermediateRewards { get; init; } = true;

    /// <summary>
    /// Evidence source label for reinforcements created by propagation.
    /// Default: <c>"reward-propagation"</c>.
    /// </summary>
    public string EvidenceSource { get; init; } = "reward-propagation";
}
```

#### `IExplorationStrategy` — exploration during action selection

```csharp
/// <summary>
/// Balances exploitation (choosing the best-known action) with exploration
/// (trying uncertain actions) during action selection. Domain-agnostic —
/// operates on action scores and uncertainty estimates.
/// </summary>
public interface IExplorationStrategy
{
    /// <summary>
    /// Selects an action index given exploitation scores and exploration
    /// signals for each candidate action.
    /// </summary>
    /// <param name="actions">
    /// Candidate actions with exploitation scores and uncertainty estimates.
    /// </param>
    /// <param name="totalSelections">
    /// Total number of action selections made so far (for annealing).
    /// </param>
    /// <returns>Index into <paramref name="actions"/> of the selected action.</returns>
    int SelectAction(IReadOnlyList<ActionCandidate> actions, int totalSelections);
}

/// <summary>A candidate action with exploitation score and uncertainty.</summary>
public sealed record ActionCandidate
{
    /// <summary>Exploitation score (e.g., mean recalled reward for this action).</summary>
    public required float Score { get; init; }

    /// <summary>
    /// Uncertainty estimate (e.g., variance of recalled rewards).
    /// Higher values mean less is known about this action.
    /// </summary>
    public required float Uncertainty { get; init; }

    /// <summary>Number of times this action has been selected in the past.</summary>
    public required int SelectionCount { get; init; }
}
```

#### `TagImportanceMap` — learned feature weights

```csharp
/// <summary>
/// Tracks correlation between semantic tags and positive outcomes across
/// empirical entries. Used to boost discriminating tags at recall time.
/// Produced by the offline learner's feature importance sweep.
/// </summary>
public sealed record TagImportanceMap
{
    /// <summary>
    /// Tag key → importance score in [0.0, 1.0]. Tags not present have
    /// neutral importance (1.0 — no boost or penalty).
    /// </summary>
    public required IReadOnlyDictionary<string, float> Importances { get; init; }

    /// <summary>Number of entries analyzed to produce this map.</summary>
    public required int SampleSize { get; init; }

    /// <summary>When this map was last computed.</summary>
    public required DateTimeOffset ComputedAt { get; init; }
}
```

### Extended `SimulationOutcome`

```csharp
// New optional property on SimulationOutcome

/// <summary>
/// Optional trajectory of states visited during simulation. When provided,
/// the offline learner can perform temporal credit assignment on simulated
/// episodes rather than treating the outcome as a single scalar.
/// </summary>
public IReadOnlyList<EpisodeStep>? Trajectory { get; init; }
```

### Portable skill packaging types

#### `LearnedSkillPackage` — the portable artifact

```csharp
/// <summary>
/// A portable, serializable bundle of everything an agent learned about a
/// domain. Contains curated empirical entries, episode trajectories,
/// consolidated knowledge, and learned feature weights — everything needed
/// to reconstruct the skill in a fresh agent without retraining.
/// </summary>
public sealed record LearnedSkillPackage
{
    /// <summary>Unique package identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable skill name (e.g., "connect4-strategy").</summary>
    public required string Name { get; init; }

    /// <summary>
    /// Domain identifier — scopes the skill to a problem space.
    /// Importing agents should use compatible feature extraction for the domain.
    /// Examples: <c>"connect4"</c>, <c>"incident-triage"</c>, <c>"chess"</c>.
    /// </summary>
    public required string Domain { get; init; }

    /// <summary>Semantic version (MAJOR.MINOR.PATCH). Tracks skill improvement.</summary>
    public required string Version { get; init; }

    /// <summary>Optional description of what this skill knows and how it was trained.</summary>
    public string? Description { get; init; }

    // ── Learned content ──────────────────────────────────────────

    /// <summary>
    /// Curated empirical entries — patterns, heuristics, and procedural
    /// skills that passed export quality gates (min strength, confidence,
    /// observations).
    /// </summary>
    public required IReadOnlyList<EmpiricalEntry> Entries { get; init; }

    /// <summary>
    /// Episode trajectories that produced the knowledge. Useful for replay,
    /// audit, and re-propagation. May be empty for skills exported without
    /// trajectory history.
    /// </summary>
    public IReadOnlyList<Episode> Episodes { get; init; } = [];

    /// <summary>
    /// Knowledge documents promoted from mature empirical entries via
    /// consolidation. These are the highest-confidence, most stable beliefs.
    /// </summary>
    public IReadOnlyList<KnowledgeDocument> Knowledge { get; init; } = [];

    /// <summary>
    /// Learned feature importance weights. When present, the importing agent
    /// can use these to boost discriminating tags at recall time without
    /// re-learning which features matter.
    /// </summary>
    public TagImportanceMap? TagImportances { get; init; }

    // ── Provenance ───────────────────────────────────────────────

    /// <summary>Training provenance, statistics, and configuration.</summary>
    public required TrainingManifest Manifest { get; init; }
}

/// <summary>
/// Provenance and statistics for a <see cref="LearnedSkillPackage"/>.
/// Records how the skill was produced so consumers can assess quality
/// and compatibility.
/// </summary>
public sealed record TrainingManifest
{
    /// <summary>Total episodes the agent played/experienced during training.</summary>
    public required int TotalEpisodes { get; init; }

    /// <summary>Total empirical entries produced (before quality filtering).</summary>
    public required int TotalEntries { get; init; }

    /// <summary>Average terminal reward across training episodes.</summary>
    public float AverageReward { get; init; }

    /// <summary>When the package was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Wall-clock training duration.</summary>
    public TimeSpan TrainingDuration { get; init; }

    /// <summary>
    /// Key-value training statistics (e.g., <c>"win_rate" → "0.72"</c>,
    /// <c>"games_played" → "500"</c>).
    /// </summary>
    public IReadOnlyDictionary<string, string> Statistics { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Key-value configuration used during training (e.g.,
    /// <c>"discount_factor" → "0.95"</c>, <c>"exploration" → "ucb"</c>).
    /// Helps consumers understand what produced this skill.
    /// </summary>
    public IReadOnlyDictionary<string, string> Configuration { get; init; }
        = new Dictionary<string, string>();
}
```

#### `ISkillPackager` — export and import

```csharp
/// <summary>
/// Exports learned knowledge from memory stores into a portable
/// <see cref="LearnedSkillPackage"/> and imports packages into fresh
/// memory stores. The export path applies quality gates; the import
/// path applies trust scaling.
/// </summary>
public interface ISkillPackager
{
    /// <summary>
    /// Gathers entries from empirical memory (filtered by quality gates),
    /// episodes, consolidated knowledge, and tag importance into a
    /// serializable package.
    /// </summary>
    Task<LearnedSkillPackage> ExportAsync(
        SkillExportOptions options,
        IEmpiricalMemory memory,
        IEpisodeStore? episodes = null,
        IKnowledgeStore? knowledge = null,
        TagImportanceMap? tagImportances = null,
        CancellationToken ct = default);

    /// <summary>
    /// Loads a skill package into memory stores. Entries are committed with
    /// configurable strength scaling and merge behavior.
    /// </summary>
    Task<SkillImportResult> ImportAsync(
        LearnedSkillPackage package,
        IEmpiricalMemory memory,
        IEpisodeStore? episodes = null,
        IKnowledgeStore? knowledge = null,
        SkillImportOptions? options = null,
        CancellationToken ct = default);
}

/// <summary>Controls what gets included in an exported skill package.</summary>
public sealed record SkillExportOptions
{
    /// <summary>Skill name for the package.</summary>
    public required string Name { get; init; }

    /// <summary>Domain identifier.</summary>
    public required string Domain { get; init; }

    /// <summary>Package version. Default: <c>"1.0.0"</c>.</summary>
    public string Version { get; init; } = "1.0.0";

    /// <summary>Optional description.</summary>
    public string? Description { get; init; }

    // ── Quality gates ────────────────────────────────────────

    /// <summary>Minimum entry strength for inclusion. Default: 0.3.</summary>
    public float MinStrength { get; init; } = 0.3f;

    /// <summary>Minimum entry confidence for inclusion. Default: 0.2.</summary>
    public float MinConfidence { get; init; } = 0.2f;

    /// <summary>Minimum observation count for inclusion. Default: 2.</summary>
    public int MinObservations { get; init; } = 2;

    /// <summary>When set, only entries with at least one of these tags are included.</summary>
    public IReadOnlyList<string>? RequiredTags { get; init; }

    /// <summary>When set, only entries of this kind are included.</summary>
    public EmpiricalKind? Kind { get; init; }

    /// <summary>Whether to include episode trajectories. Default: true.</summary>
    public bool IncludeEpisodes { get; init; } = true;

    /// <summary>Whether to include consolidated knowledge documents. Default: true.</summary>
    public bool IncludeKnowledge { get; init; } = true;
}

/// <summary>Controls how an imported skill package merges with existing memory.</summary>
public sealed record SkillImportOptions
{
    /// <summary>
    /// How to handle conflicts between imported and existing entries.
    /// Default: <see cref="SkillImportMode.Merge"/>.
    /// </summary>
    public SkillImportMode Mode { get; init; } = SkillImportMode.Merge;

    /// <summary>
    /// Strength multiplier applied to imported entries. Values below 1.0
    /// discount imported knowledge (the importing agent didn't produce the
    /// evidence firsthand). Default: 0.8.
    /// </summary>
    public float StrengthScale { get; init; } = 0.8f;

    /// <summary>
    /// Optional prefix prepended to imported entry IDs to avoid collisions.
    /// When <see langword="null"/>, IDs are imported as-is.
    /// </summary>
    public string? IdPrefix { get; init; }

    /// <summary>
    /// Evidence source label for imported entries.
    /// Default: <c>"skill-import"</c>.
    /// </summary>
    public string EvidenceSource { get; init; } = "skill-import";
}

/// <summary>Import conflict resolution mode.</summary>
public enum SkillImportMode
{
    /// <summary>Merge with existing entries — reinforce on semantic match, add otherwise.</summary>
    Merge,
    /// <summary>Replace existing entries with imported ones in case of ID collision.</summary>
    Replace,
    /// <summary>Only add entries that don't already exist (skip duplicates).</summary>
    AddOnly
}

/// <summary>Summary of a skill import operation.</summary>
public sealed record SkillImportResult
{
    /// <summary>Entries added to empirical memory.</summary>
    public required int EntriesAdded { get; init; }

    /// <summary>Entries merged (reinforced existing matches).</summary>
    public required int EntriesMerged { get; init; }

    /// <summary>Entries skipped (duplicates in AddOnly mode).</summary>
    public required int EntriesSkipped { get; init; }

    /// <summary>Episodes imported.</summary>
    public required int EpisodesImported { get; init; }

    /// <summary>Knowledge documents upserted.</summary>
    public required int KnowledgeDocumentsImported { get; init; }
}
```

#### `ISkillPackageFormat` — serialization

```csharp
/// <summary>
/// Serializes and deserializes <see cref="LearnedSkillPackage"/> to/from
/// streams. Implementations may use JSON, MessagePack, protobuf, or any
/// other format. Ships with <c>JsonSkillPackageFormat</c> as the default.
/// </summary>
public interface ISkillPackageFormat
{
    /// <summary>Content type identifier (e.g., <c>"application/json"</c>).</summary>
    string ContentType { get; }

    /// <summary>Serializes a package to the output stream.</summary>
    Task SerializeAsync(
        LearnedSkillPackage package, Stream output, CancellationToken ct = default);

    /// <summary>Deserializes a package from the input stream.</summary>
    Task<LearnedSkillPackage> DeserializeAsync(
        Stream input, CancellationToken ct = default);
}
```

---

## Consequences

### Positive

- **Temporal structure enables real skill learning.** Agents can learn that
  early decisions contribute to late outcomes, dramatically improving
  performance in sequential tasks like games.
- **No breaking changes.** All additions are new types or optional properties.
  Existing consumers of `IEmpiricalMemory` continue to work unchanged.
- **Reusable across domains.** The episode/credit/exploration abstractions
  are domain-agnostic — Connect4, chess, incident investigation, planning.
- **Immediate validation path.** The Connect4 demo can adopt episodes and
  credit assignment incrementally, providing measurable improvement.
- **Exploration bonus uses existing data.** `Variance` on `EmpiricalEntry` is
  already computed; the exploration strategy simply exposes it at decision time.
- **Learning becomes a reusable asset.** Skill packaging transforms runtime
  memory state into versioned, shareable artifacts. An agent that spent 500
  games learning Connect4 can export that skill for another agent to use
  immediately — no retraining required.
- **Clean separation of concerns.** Export handles quality filtering
  (what’s worth keeping); import handles trust calibration (how much to
  trust foreign knowledge). Neither pollutes the core memory interfaces.

### Negative

- **Increased conceptual surface.** New types (`Episode`, `IEpisodeStore`,
  `IRewardPropagator`, `IExplorationStrategy`) add to the learning vocabulary.
  Mitigation: clear documentation and the Connect4 demo as a worked example.
- **Episode storage overhead.** Storing full trajectories requires more memory
  than individual entries. Mitigation: episodes can be pruned after credit
  assignment; only entries persist long-term.
- **Discount factor tuning.** The `γ` parameter significantly affects learning
  behavior. Too high (0.99) gives nearly equal credit to all steps; too low
  (0.5) ignores early moves. Mitigation: configurable via
  `RewardPropagationOptions` with a sensible default (0.95).
- **Package compatibility burden.** Imported skills are only useful if the
  importing agent uses compatible feature extraction for the domain. A
  Connect4 skill exported with `BoardFeatures.Decompose` tags is meaningless
  to an agent with a different feature schema. Mitigation: the
  `TrainingManifest` records domain and configuration; import can validate
  compatibility before committing entries.
- **Serialization stability.** Changing `EmpiricalEntry` or `Episode` record
  shapes in future versions may break deserialization of older packages.
  Mitigation: include a format version in `LearnedSkillPackage`; the
  serializer can handle backward-compatible migrations.

### Neutral

- The policy abstraction (`IPolicy<TState, TAction>`) is intentionally
  deferred. The exploration strategy provides the immediate benefit of
  exploration vs. exploitation without the complexity of generic state/action
  typing.
- Learned skill packages and the `ISkillCatalog` (ADR-011) operate in
  adjacent but separate spaces. A future bridge could register exported
  packages in a catalog, making learned skills discoverable alongside
  external tools. This convergence is deferred.

---

## Related ADRs

| ADR | Relationship |
|---|---|
| ADR-007 | Established `IEmpiricalMemory` and `IOfflineLearner` |
| ADR-008 | Added affective signals, prediction-error reinforcement, and decay |
| ADR-011 | Skill catalog — discovers external tools; future bridge for learned skill registration |
| ADR-013 | Agentic patterns — loop primitive needed for iterative skill improvement |
