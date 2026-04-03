# ADR-008: Implementation Plan — Affective Signals and Autonomous Learning

| Field         | Value                                                              |
|---------------|--------------------------------------------------------------------|
| **Status**    | Proposed                                                           |
| **Date**      | 2025-07-28                                                         |
| **Relates to**| ADR-008 (affective signals), ADR-007 (background cognitive processes) |

---

## Architecture Overview

The learning model decomposes into two execution contexts:

```
┌─────────────────────────────────────────────────────────────────────┐
│  WAKING — in-process, synchronous with agent/human interaction      │
│                                                                     │
│  Agent loop:                                                        │
│    RecallAsync() → apply knowledge → observe outcome → ReinforceAsync()
│                                                                     │
│  Primitives: IEmpiricalMemory, EmpiricalEntry, AffectOptions        │
│  Lives in: Ananke.Orchestration (library code)                      │
│  Runs: during conversations, tool calls, game moves                 │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              │ shared IEmpiricalMemory (Qdrant or in-memory)
                              │
┌─────────────────────────────────────────────────────────────────────┐
│  DREAMING — external service, runs independently                    │
│                                                                     │
│  Background loops:                                                  │
│    Decay sweep → forget weak/unstable beliefs                       │
│    Curiosity walk → explore high-surprise entries                   │
│    Consolidation → promote stable patterns to IKnowledgeStore       │
│                                                                     │
│  Primitives: IDreamer, DreamerOptions, intrinsic reward functions   │
│  Lives in: Ananke.Orchestration (interface + logic)                 │
│            Application host (service registration + scheduling)     │
│  Runs: on a timer, or triggered by entry count / surprise threshold │
└─────────────────────────────────────────────────────────────────────┘
```

### Why an external service ("sleep mode")

The dreaming process is **not** tied to any conversation or agent session:

| Concern | Waking (agent loop) | Dreaming (background service) |
|---|---|---|
| Trigger | User input, tool result | Timer, threshold, idle detection |
| Duration | Milliseconds per call | Minutes per sweep |
| Scope | One entry at a time | All entries (decay), exploration set (curiosity) |
| Side effects | Reinforce/contradict single entry | Bulk update strengths, delete entries, promote to knowledge store |
| Delivery | Immediate (conversation) | Asynchronous (email, queued insight, next-session notification) |
| Cancellation | Per-request | Per-sweep, interruptible |

The existing `ProducerConsumer<T>` + `IBackgroundWorker<T>` pattern or a
standard `IHostedService` / `BackgroundService` can host this. It reads and
writes to `IEmpiricalMemory` (and optionally `IKnowledgeStore`) via the same
interfaces the agent uses — no new storage layer.

### Delivery already exists

When the dreamer discovers something, it needs to tell someone. Ananke already
has these paths:

| Path | Mechanism | When to use |
|---|---|---|
| Active chat session | `StateMachine.SignalInsightAsync` → `OnInsight` handler → SSE | User is online, in a conversation |
| Buffered delivery | `OnInsight` handler checks state, enqueues for next pause | User is online but busy |
| Offline delivery | Application-level: email, queue, notification API | No active session |
| Agent tool surface | `EmpiricalMemoryTools.recall_empirical` returns dreamer's discoveries on next conversation | User starts a new session |

The Connect4 demo already demonstrates `SignalInsightAsync` delivery:

```csharp
// From Connect4Demo/Program.cs — insights delivered via state machine
machine.OnInsight<string>((insight, state) =>
{
    display.AddInsight(insight);
    return Task.CompletedTask;
});

// From GameAnalyzer — discoveries signaled after analysis
foreach (var insight in insights)
    await machine.SignalInsightAsync(insight);
```

The dreamer follows the same pattern but runs on its own schedule.

---

## Phase 0: Signal Fields on `EmpiricalEntry`

**Goal**: Add the affective signal fields as optional properties. Zero logic
changes. All existing code continues to work.

### Changes

#### `Ananke.Orchestration/Memory/EmpiricalTypes.cs`

Add to `EmpiricalEntry`:

```csharp
// ── Affective signals (ADR-008) ──────────────────────────────────

/// <summary>
/// Belief strength — driven by prediction-error-modulated reinforcement.
/// Decays over time; entries below the configured threshold are candidates
/// for removal. Distinct from <see cref="Confidence"/>, which is derived
/// from prediction error variance when affective signals are active.
/// Default: <c>0.5</c>.
/// </summary>
public float Strength { get; init; } = 0.5f;

/// <summary>
/// Outcome direction: <c>-1.0</c> (negative outcome) to <c>+1.0</c>
/// (positive outcome). Influences recall priority, not truth.
/// </summary>
public float Valence { get; init; }

/// <summary>
/// Outcome intensity: <c>0.0</c> (trivial) to <c>1.0</c> (critical).
/// Influences recall priority, not truth.
/// </summary>
public float Intensity { get; init; }

/// <summary>
/// Exponential moving average of squared prediction errors.
/// Used to derive <see cref="Confidence"/>: <c>1 / (1 + Variance)</c>
/// when affective reinforcement is active.
/// Default: <c>1.0</c> (maximum uncertainty).
/// </summary>
public float Variance { get; init; } = 1.0f;

/// <summary>
/// Most recent prediction error <c>(|predicted − actual|)</c>.
/// Stored for diagnostics and reinforcement cooldown.
/// </summary>
public float LastPredictionError { get; init; }
```

Add `Reward` to `Reinforcement`:

```csharp
/// <summary>
/// Actual outcome value for prediction-error computation. When provided,
/// the implementation computes prediction error as
/// <c>|entry.Confidence − Reward|</c> and modulates reinforcement.
/// When <see langword="null"/>, falls back to flat confidence adjustment.
/// </summary>
public float? Reward { get; init; }
```

#### `Ananke.Qdrant/QdrantEmpiricalMemory.cs`

Add payload field constants and include them in `BuildPayload`/`MapPoint`:

```csharp
private const string StrengthKey = "strength";
private const string ValenceKey = "valence";
private const string IntensityKey = "intensity";
private const string VarianceKey = "variance";
private const string LastPredictionErrorKey = "last_prediction_error";
```

No logic changes to `ReinforceAsync` or `RecallAsync` yet — the fields are
stored and round-tripped but don't affect behavior.

### Test changes

Update `InMemoryEmpiricalMemoryTests` to verify the new fields round-trip
through `CommitAsync` → `GetAsync`.

### Validation

- `run_build` passes
- Existing tests pass unchanged
- Connect4Demo compiles and runs (entries committed without signal fields
  get default values: `Strength = 0.5`, `Variance = 1.0`, etc.)

---

## Phase 1: `AffectOptions` and Prediction-Error Reinforcement

**Goal**: Replace flat confidence bumps with prediction-error-modulated
reinforcement when `Reward` is provided. Backward-compatible — `Reward = null`
preserves current behavior.

### New type: `AffectOptions`

File: `Ananke.Orchestration/Memory/EmpiricalTypes.cs`

```csharp
/// <summary>
/// Configuration for affect-driven learning mechanics:
/// prediction-error reinforcement, decay, and priority boosting.
/// </summary>
public sealed record AffectOptions
{
    /// <summary>Base learning rate for strength reinforcement. Default: 0.1.</summary>
    public float LearningRate { get; init; } = 0.1f;

    /// <summary>
    /// EMA smoothing factor for variance. Higher values weight recent
    /// errors more. Range (0, 1). Default: 0.1.
    /// </summary>
    public float VarianceSmoothingFactor { get; init; } = 0.1f;

    /// <summary>Per-cycle multiplicative decay applied to strength. Default: 0.98.</summary>
    public float BaseDecayRate { get; init; } = 0.98f;

    /// <summary>
    /// Variance-amplified decay multiplier. Unstable beliefs decay faster.
    /// Default: 0.02.
    /// </summary>
    public float VarianceDecayRate { get; init; } = 0.02f;

    /// <summary>Strength below which entries are candidates for removal. Default: 0.05.</summary>
    public float DeletionThreshold { get; init; } = 0.05f;

    /// <summary>
    /// Max recall priority boost from valence × intensity.
    /// Applied as: <c>score × (1 + MaxPriorityBoost × intensity × |valence|)</c>.
    /// Default: 0.3 (up to 30% boost).
    /// </summary>
    public float MaxPriorityBoost { get; init; } = 0.3f;

    /// <summary>
    /// Minimum hours between full-strength reinforcements for the same entry.
    /// Prevents frequency-driven self-reinforcement loops. Default: 1.0.
    /// </summary>
    public float ReinforcementCooldownHours { get; init; } = 1.0f;
}
```

### Modified: `InMemoryEmpiricalMemory`

**Constructor** — accept optional `AffectOptions`:

```csharp
public InMemoryEmpiricalMemory(
    IEmbeddingModel embedder,
    float dedupThreshold = 0.9f,
    TimeDecayOptions? decayOptions = null,
    AffectOptions? affectOptions = null,     // ← new
    ILogger<InMemoryEmpiricalMemory>? logger = null)
```

**`ReinforceAsync`** — two paths:

```csharp
if (reinforcement.Reward is not null && _affectOptions is not null)
{
    // ── Prediction-error path ────────────────────────────────
    float predicted = stored.Entry.Confidence;
    float actual = reinforcement.Reward.Value;
    float predictionError = MathF.Abs(predicted - actual);

    // Cooldown
    float hours = (float)(DateTimeOffset.UtcNow - stored.Entry.LastObserved).TotalHours;
    float cooldown = MathF.Min(1f, hours / _affectOptions.ReinforcementCooldownHours);

    // Strength: confirming ≈ +lr, maximally surprising ≈ 0
    float strengthDelta = _affectOptions.LearningRate * (1f - predictionError) * cooldown;

    // Variance: EMA of squared prediction errors
    float a = _affectOptions.VarianceSmoothingFactor;
    float newVariance = (1f - a) * stored.Entry.Variance + a * predictionError * predictionError;

    // Confidence derived from variance
    float newConfidence = 1f / (1f + newVariance);

    // Priority signals (do NOT affect truth)
    float newValence = MathF.Clamp(actual, -1f, 1f);
    float newIntensity = MathF.Clamp(MathF.Abs(actual), 0f, 1f);

    updated = stored.Entry with
    {
        Strength = MathF.Max(0f, stored.Entry.Strength + strengthDelta),
        Confidence = newConfidence,
        Variance = newVariance,
        Valence = newValence,
        Intensity = newIntensity,
        LastPredictionError = predictionError,
        ObservationCount = stored.Entry.ObservationCount + 1,
        LastObserved = DateTimeOffset.UtcNow,
        Evidence = TrimEvidence([.. stored.Entry.Evidence, .. reinforcement.NewEvidence])
    };
}
else
{
    // ── Flat path (backward compatible) ──────────────────────
    var adjustment = reinforcement.ConfidenceAdjustment ?? 0.1f;
    updated = stored.Entry with
    {
        Confidence = Math.Min(1.0f, stored.Entry.Confidence + adjustment),
        ObservationCount = stored.Entry.ObservationCount + 1,
        LastObserved = DateTimeOffset.UtcNow,
        Evidence = TrimEvidence([.. stored.Entry.Evidence, .. reinforcement.NewEvidence])
    };
}
```

### Modified: `QdrantEmpiricalMemory`

Same two-path logic in `ReinforceAsync`. Uses `SetPayloadAsync` for both paths
(no re-embedding). New payload fields written when the prediction-error path
is taken.

### New tests

| Test | Validates |
|---|---|
| `Reinforce_WithReward_UpdatesStrengthByPredictionError` | High confidence + confirming reward → small strength increase |
| `Reinforce_WithReward_HighSurprise_MinimalStrengthIncrease` | Low confidence + unexpected reward → near-zero strength delta |
| `Reinforce_WithReward_UpdatesVarianceViaEMA` | Variance converges toward 0 with consistent low errors |
| `Reinforce_WithReward_DerivesConfidenceFromVariance` | `confidence ≈ 1 / (1 + variance)` after reinforcement |
| `Reinforce_WithReward_CooldownReducesEffect` | Two rapid reinforcements → second has reduced effect |
| `Reinforce_WithoutReward_PreservesCurrentBehavior` | `Reward = null` → flat `+0.1` confidence bump |
| `Reinforce_WithReward_SetsValenceAndIntensity` | Positive reward → positive valence; magnitude → intensity |

### Connect4Demo update

The `GameAnalyzer` can start providing `Reward` on reinforcements:

```csharp
// In AnalyzeWinAsync — reinforcing recalled patterns after a win
await memory.ReinforceAsync(match.Entry.Id, new Reinforcement
{
    NewEvidence = [$"game-{gameNumber}: won while applying this"],
    Source = "game-analysis",
    Reward = 1.0f    // positive outcome — the agent won
});

// In AnalyzeLossAsync — contradicting or negatively reinforcing
await memory.ReinforceAsync(match.Entry.Id, new Reinforcement
{
    NewEvidence = [$"game-{gameNumber}: lost despite applying this"],
    Source = "game-analysis",
    Reward = -0.5f   // negative outcome — applied but still lost
});
```

---

## Phase 2: Priority Boost in Recall Scoring

**Goal**: Valence and intensity influence which entries surface first, without
affecting the truth path.

### Modified: `InMemoryEmpiricalMemory.RecallAsync`

```csharp
// Current
var compositeScore = vectorScore * stored.Entry.Confidence * recencyWeight;

// New — when AffectOptions is configured
if (_affectOptions is not null)
{
    var priorityBoost = 1f + _affectOptions.MaxPriorityBoost
                           * stored.Entry.Intensity
                           * MathF.Abs(stored.Entry.Valence);
    compositeScore *= priorityBoost;
}
```

### Same change in `QdrantEmpiricalMemory.RecallAsync`

Client-side composite scoring already happens after Qdrant returns results.
Add the priority boost to the existing rescore loop.

### New tests

| Test | Validates |
|---|---|
| `Recall_WithAffectOptions_HighIntensityBoosted` | High-intensity entry ranks above equal-confidence low-intensity entry |
| `Recall_WithAffectOptions_BoostCappedByMaxPriorityBoost` | Boost never exceeds `1 + MaxPriorityBoost` multiplier |
| `Recall_WithoutAffectOptions_NoPriorityBoost` | No `AffectOptions` → current behavior exactly |

---

## Phase 3: The Dreamer — External Background Service

**Goal**: An independent process that handles decay (forgetting), curiosity-
driven exploration (wandering), and eventually consolidation (abstraction).
Structured as a service that operates on `IEmpiricalMemory` — same interface
the agent uses.

### Conceptual model

```
┌───────────────────────────────────────────────────────────────────────┐
│                         The Dreamer                                    │
│                                                                       │
│  ┌─────────────┐   ┌─────────────────┐   ┌─────────────────────────┐ │
│  │   DECAY      │   │   CURIOSITY     │   │   CONSOLIDATION         │ │
│  │   (forget)   │   │   (wander)      │   │   (abstract)            │ │
│  │              │   │                 │   │                         │ │
│  │  Sweep all   │   │  Pick high-     │   │  Find strong + stable   │ │
│  │  entries:    │   │  surprise or    │   │  + frequently-used      │ │
│  │  decay       │   │  random entry   │   │  entries → promote to   │ │
│  │  strength,   │   │  → form         │   │  IKnowledgeStore        │ │
│  │  remove      │   │  prediction     │   │                         │ │
│  │  below       │   │  → search for   │   │  (Phase 6 — deferred)   │ │
│  │  threshold   │   │  evidence:      │   │                         │ │
│  │              │   │    reflective   │   │                         │ │
│  │              │   │    (data search) │   │                         │ │
│  │              │   │    + simulated   │   │                         │ │
│  │              │   │    (imagination) │   │                         │ │
│  │              │   │  → compute      │   │                         │ │
│  │              │   │  intrinsic      │   │                         │ │
│  │              │   │  reward         │   │                         │ │
│  │              │   │  → reinforce    │   │                         │ │
│  │              │   │  or contradict  │   │                         │ │
│  └──────┬───────┘   └──────┬──────────┘   └─────────────────────────┘ │
│         │                  │                                          │
│         ▼                  ▼                                          │
│  IEmpiricalMemory    IEmpiricalMemory                                 │
│  (bulk updates)      + IKnowledgeStore (search for evidence)          │
│                      + IEmbeddingModel (form predictions)             │
│                      + ISimulationSource (optional — imagined evidence)│
│                            │                                          │
│                            ▼                                          │
│                   SignalInsightAsync / email / queue                   │
│                   (when discovery is worth reporting)                  │
└───────────────────────────────────────────────────────────────────────┘
```

### Interface

File: `Ananke.Orchestration/Memory/IDreamer.cs`

```csharp
/// <summary>
/// Background learning service that operates on <see cref="IEmpiricalMemory"/>
/// independently of active conversations. Handles forgetting (decay),
/// curiosity-driven exploration (wandering), and eventually consolidation
/// (abstraction promotion).
/// </summary>
/// <remarks>
/// Analogous to sleep consolidation in neuroscience: a periodic process
/// that strengthens stable memories, prunes uncertain ones, and discovers
/// connections that weren't visible during active use.
/// Implementations may be hosted as <c>IHostedService</c>, scheduled via
/// a timer, or invoked manually (e.g., between games in the Connect4 demo).
/// </remarks>
public interface IDreamer
{
    /// <summary>
    /// Runs one full dream cycle: decay → curiosity walk → (future: consolidation).
    /// Returns a summary of what happened.
    /// </summary>
    Task<DreamResult> DreamAsync(CancellationToken ct = default);

    /// <summary>
    /// Runs only the decay sweep. Useful when called on a separate schedule
    /// from exploration.
    /// </summary>
    Task<int> DecayAsync(CancellationToken ct = default);
}

/// <summary>Summary of a single dream cycle.</summary>
public sealed record DreamResult
{
    /// <summary>Entries removed by the decay sweep.</summary>
    public required int Decayed { get; init; }

    /// <summary>Entries explored during the curiosity walk.</summary>
    public required int Explored { get; init; }

    /// <summary>Entries reinforced by intrinsic reward (prediction confirmed).</summary>
    public required int Reinforced { get; init; }

    /// <summary>Entries contradicted (prediction failed).</summary>
    public required int Contradicted { get; init; }

    /// <summary>
    /// Discoveries worth reporting. Each is a natural-language summary
    /// suitable for delivery via <c>SignalInsightAsync</c>, email, etc.
    /// </summary>
    public required IReadOnlyList<string> Discoveries { get; init; }
}
```

### Configuration

```csharp
/// <summary>Configuration for the dreamer service.</summary>
public sealed record DreamerOptions
{
    /// <summary>
    /// How many entries to explore per curiosity walk. Default: 5.
    /// Higher values = more thorough but slower cycles.
    /// </summary>
    public int ExplorationBatchSize { get; init; } = 5;

    /// <summary>
    /// Selection bias for curiosity walk. Entries with prediction error
    /// above this threshold are preferred for exploration. Default: 0.5.
    /// </summary>
    public float CuriosityThreshold { get; init; } = 0.5f;

    /// <summary>
    /// Fraction of exploration batch reserved for random entries
    /// (ε-greedy exploration). Default: 0.2 (1 in 5 is random).
    /// </summary>
    public float ExplorationRandomFraction { get; init; } = 0.2f;

    /// <summary>
    /// Minimum score improvement over prediction to count as a discovery
    /// worth reporting. Default: 0.3.
    /// </summary>
    public float DiscoveryThreshold { get; init; } = 0.3f;

    /// <summary>Affect options to use for reinforcement and decay.</summary>
    public AffectOptions Affect { get; init; } = new();

    /// <summary>
    /// Max simulation episodes per explored entry. Only used when an
    /// <see cref="ISimulationSource"/> is provided. Default: 20.
    /// </summary>
    public int MaxSimulationEpisodes { get; init; } = 20;

    /// <summary>
    /// Minimum entry confidence before simulation is attempted.
    /// Very low-confidence entries should accumulate reflective evidence
    /// before spending simulation budget. Default: 0.2.
    /// </summary>
    public float SimulationMinConfidence { get; init; } = 0.2f;

    /// <summary>
    /// Weight of simulation evidence relative to reflective (real-data)
    /// evidence when combining rewards. Real data should always dominate.
    /// Default: 0.3 (simulation counts for 30% of reflective weight).
    /// </summary>
    public float SimulationEvidenceWeight { get; init; } = 0.3f;
}
```

### Implementation: `InMemoryDreamer`

File: `Ananke.Orchestration/Memory/InMemoryDreamer.cs`

The dreamer operates entirely through `IEmpiricalMemory` and `IEmbeddingModel`.
It does not access internal storage directly.

#### Decay sweep

Iterate all entries via a new `IEmpiricalMemory` method (see interface
evolution below), apply strength decay, soft-delete below threshold:

```csharp
public async Task<int> DecayAsync(CancellationToken ct)
{
    // Recall broadly to get all entries (or use a scroll/iterate method)
    // Apply: strength = strength * baseDecay - variance * varianceDecay
    // Contradict entries below threshold (soft-delete via confidence → 0)
    // Return count of removed entries
}
```

**Interface evolution needed**: `IEmpiricalMemory` currently has no way to
iterate all entries. Options:

| Option | Pros | Cons |
|---|---|---|
| A. Add `BrowseAsync(int offset, int limit)` to `IEmpiricalMemory` | Clean, paginated | Interface change |
| B. Use `RecallAsync("")` with large `TopK` and `MinConfidence = 0` | No interface change | Semantically wrong; depends on empty query behavior |
| C. Add `DecayAsync(AffectOptions)` to `IEmpiricalMemory` directly | Decay is a first-class operation | Mixes storage with business logic |
| D. Expose `DecayAsync` only on concrete implementations | No interface change | Dreamer must know the concrete type |

**Recommended: Option A** — add `BrowseAsync` to the interface. This is the
same pattern as `IKnowledgeCatalog.BrowseAsync` which already exists in the
codebase. Decay, exploration, and consolidation all need to iterate entries.

```csharp
// Addition to IEmpiricalMemory
/// <summary>
/// Iterates entries in pages, optionally filtered by kind.
/// Used by background processes for decay sweeps and exploration.
/// </summary>
Task<IReadOnlyList<EmpiricalEntry>> BrowseAsync(
    int offset, int limit, EmpiricalKind? kind = null, CancellationToken ct = default);
```

#### Curiosity walk

```
for each entry in exploration batch:
    1. Select entry (high surprise preferred, some random)
    2. Form prediction vector:
       - Recall top-3 similar entries
       - Predicted = weighted average of their embeddings (by confidence)
       - If no similar entries: predicted = entry's own embedding + noise

    3a. Reflective evidence (search existing data):
        - Use entry's Condition (for patterns) or Goal (for skills) as query
        - Search IKnowledgeStore for supporting/contradicting data
        - Search IEmpiricalMemory for related patterns

    3b. Simulated evidence (if ISimulationSource is available):
        - Only if entry.Confidence >= SimulationMinConfidence
        - Call ISimulationSource.SimulateAsync(entry, relatedKnowledge)
        - Simulator runs domain-specific scenarios (self-play, rollouts, etc.)
        - Returns SimulationOutcome with reward and episode counts

    4. Combine evidence:
       - Reflective: actual = cosine_similarity(evidence_centroid, predicted_vector)
       - Simulated: outcome.Reward weighted by SimulationEvidenceWeight
       - Combined reward = weighted average (real data always dominates)
       - If only one source available, use that source alone

    5. Compute intrinsic reward:
       - If surprising AND coherent with existing knowledge → high reward (discovery)
       - If surprising AND incoherent → low reward (noise)
       - If expected AND coherent → small positive (confirmation)
       - If expected AND incoherent → negative (this shouldn't happen — contradiction)
    6. Reinforce or contradict:
       - Call ReinforceAsync with computed Reward
       - Or ContradictAsync if evidence directly contradicts
    7. If reward > discovery_threshold:
       - Add to discoveries list for reporting
```

#### Intrinsic reward computation

```csharp
float ComputeIntrinsicReward(
    float surprise,           // |predicted - actual| in vector space
    float coherenceWithKnown, // how well actual outcome fits existing entries
    EmpiricalEntry entry)
{
    // 2×2 matrix:
    // Surprising + Coherent    → discovery (+0.7 to +1.0)
    // Surprising + Incoherent  → noise     (-0.3 to +0.1)
    // Expected + Coherent      → confirm   (+0.1 to +0.3)
    // Expected + Incoherent    → oddity    (-0.5 to -0.1)

    float surpriseFactor = surprise;                    // 0 = expected, 1 = maximally surprising
    float coherenceFactor = coherenceWithKnown;         // 0 = incoherent, 1 = perfectly fits

    // Discovery: surprising things that fit the broader picture
    float discoveryComponent = surpriseFactor * coherenceFactor;

    // Confirmation: expected things that continue to hold
    float confirmationComponent = (1f - surpriseFactor) * coherenceFactor * 0.3f;

    // Noise penalty: surprising but doesn't fit anything
    float noisePenalty = surpriseFactor * (1f - coherenceFactor) * -0.3f;

    // Contradiction: expected to be coherent but isn't
    float contradictionPenalty = (1f - surpriseFactor) * (1f - coherenceFactor) * -0.5f;

    return MathF.Clamp(
        discoveryComponent + confirmationComponent + noisePenalty + contradictionPenalty,
        -1f, 1f);
}
```

### Simulated Evidence: `ISimulationSource` (optional plug-in)

The curiosity walk as described above gathers **reflective** evidence — searching
existing data stores for confirmation or contradiction. But some domains have no
external evidence corpus to query. A Connect4 agent can't search a knowledge
store for "does center control win?" — the only way to test is to **play and
see**.

`ISimulationSource` is an optional, domain-specific interface that lets the
dreamer generate **imagined evidence** by running simulated scenarios (self-play,
Monte Carlo rollouts, what-if replays, etc.). The framework defines the contract;
the application provides the domain logic.

```
IDreamer (framework)
  │
  ├── Reflective walk: search IKnowledgeStore + IEmpiricalMemory
  │     → "rumination" — replaying and connecting existing knowledge
  │
  └── Simulated walk: delegate to ISimulationSource
        → "imagination" — running what-if scenarios
        │
        ├── Connect4: play N games using current empirical knowledge
        ├── Incident analysis: (no simulator — skip)
        └── Planning: run scenarios against a model
```

File: `Ananke.Orchestration/Memory/ISimulationSource.cs`

```csharp
/// <summary>
/// Domain-specific source of simulated experience. The dreamer calls this
/// during curiosity walks to generate new observations without real-world
/// interaction. Implementations are domain-specific: self-play for games,
/// Monte Carlo rollouts for planning, scenario replay for incident analysis.
/// </summary>
/// <remarks>
/// This is the "imagination" capability — analogous to mentally rehearsing
/// a scenario before acting. The dreamer uses it to test hypotheses that
/// cannot be verified by searching existing data alone.
/// <para>
/// Simulation evidence is always weighted below reflective (real-data)
/// evidence. A pattern confirmed by 50 self-play games is worth less than
/// the same pattern confirmed by 3 real games with a human. The weighting
/// is controlled by <see cref="DreamerOptions.SimulationEvidenceWeight"/>.
/// </para>
/// </remarks>
public interface ISimulationSource
{
    /// <summary>
    /// Runs a simulated scenario informed by a hypothesis and the system's
    /// current empirical knowledge. Returns the outcome for intrinsic
    /// reward computation.
    /// </summary>
    /// <param name="hypothesis">The entry being explored — the dreamer wants
    /// to know if this belief holds under simulation.</param>
    /// <param name="relatedKnowledge">Other entries recalled as context —
    /// the simulator can use these to inform strategy.</param>
    /// <param name="maxEpisodes">Maximum scenarios to run, from
    /// <see cref="DreamerOptions.MaxSimulationEpisodes"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The simulation outcome, including whether the hypothesis
    /// was supported or contradicted.</returns>
    Task<SimulationOutcome> SimulateAsync(
        EmpiricalEntry hypothesis,
        IReadOnlyList<EmpiricalMatch> relatedKnowledge,
        int maxEpisodes,
        CancellationToken ct = default);
}

/// <summary>Result of a simulated scenario.</summary>
public sealed record SimulationOutcome
{
    /// <summary>
    /// Reward signal from the simulation: positive if the hypothesis was
    /// supported, negative if contradicted. Same scale as
    /// <see cref="Reinforcement.Reward"/>.
    /// </summary>
    public required float Reward { get; init; }

    /// <summary>
    /// Natural-language description of what happened in the simulation.
    /// Used as evidence and for discovery reporting.
    /// </summary>
    public required string Summary { get; init; }

    /// <summary>
    /// Number of scenarios/episodes run.
    /// </summary>
    public required int EpisodesRun { get; init; }

    /// <summary>
    /// How many episodes supported the hypothesis.
    /// </summary>
    public required int EpisodesSupported { get; init; }
}
```

#### Evidence combination

When both reflective and simulated evidence are available, the dreamer combines
them with a configurable weight ratio:

```csharp
// Reflective evidence weight is always 1.0 (the baseline)
float reflectiveWeight = 1.0f;
float simulationWeight = _options.SimulationEvidenceWeight;  // default 0.3

float combinedReward =
    (reflectiveReward * reflectiveWeight + simulationReward * simulationWeight)
    / (reflectiveWeight + simulationWeight);
```

When only one source is available (no `IKnowledgeStore` for reflective, or no
`ISimulationSource` for simulated), the dreamer uses whatever is available.

#### Connect4 example: `Connect4Simulator`

This is what a domain-specific implementation looks like. The simulator plays
N games: an agent biased by the hypothesis vs. an agent without it, then
measures the win-rate difference:

```csharp
// In Connect4Demo (application code, NOT in the framework)
internal sealed class Connect4Simulator(
    InMemoryEmpiricalMemory memory) : ISimulationSource
{
    public async Task<SimulationOutcome> SimulateAsync(
        EmpiricalEntry hypothesis,
        IReadOnlyList<EmpiricalMatch> relatedKnowledge,
        int maxEpisodes,
        CancellationToken ct)
    {
        int supported = 0;
        for (int i = 0; i < maxEpisodes; i++)
        {
            ct.ThrowIfCancellationRequested();

            var board = new Board();
            var withAgent = new Connect4Agent(memory, forcedHypothesis: hypothesis);
            var withoutAgent = new Connect4Agent(memory, forcedHypothesis: null);

            var (p1, p2) = i % 2 == 0
                ? (withAgent, withoutAgent)
                : (withoutAgent, withAgent);

            int winner = await PlayGameAsync(board, p1, p2, ct);
            bool hypothesisWon = (i % 2 == 0 && winner == 1)
                              || (i % 2 != 0 && winner == 2);
            if (hypothesisWon) supported++;
        }

        float winRate = (float)supported / maxEpisodes;
        float reward = (winRate - 0.5f) * 2f;  // 0.5 = 0 reward, 0.8 = +0.6

        return new SimulationOutcome
        {
            Reward = reward,
            Summary = $"Self-play: '{hypothesis.Description}' → " +
                      $"{supported}/{maxEpisodes} wins ({winRate:P0})",
            EpisodesRun = maxEpisodes,
            EpisodesSupported = supported
        };
    }
}
```

#### Design constraints

- **`ISimulationSource` is optional.** The dreamer works without one — it
  skips the simulation step. Injected as a nullable dependency.
- **Simulation evidence is always discounted.** `SimulationEvidenceWeight`
  defaults to 0.3 — real data always dominates imagined data.
- **Simulation is gated by confidence.** `SimulationMinConfidence` (default
  0.2) prevents wasting simulation budget on very weak entries that should
  accumulate reflective evidence first.
- **Simulation is the most expensive part of dreaming.** Self-play of
  20 games × 5 entries = 100 games per cycle. For Connect4 (no LLM) this is
  fast. For LLM-based agents this could be slow and costly — the episode cap
  is there for budget control.
- **The framework provides the interface; the application provides the
  domain logic.** The dreamer doesn't know what a "game" or "scenario" is —
  it only knows `SimulateAsync` returns a `SimulationOutcome`.

### Hosting patterns

#### Pattern A: `IHostedService` (ASP.NET Core / Generic Host)

For applications using the generic host (web apps, distributed services):

```csharp
// Application-level hosted service — NOT in the framework library
public sealed class DreamerHostedService(IDreamer dreamer, IOptions<DreamerSchedule> schedule)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(schedule.Value.Interval, ct);
            var result = await dreamer.DreamAsync(ct);

            if (result.Discoveries.Count > 0)
            {
                // Route discoveries — application decides how
                // Option 1: SignalInsightAsync on the state machine
                // Option 2: Email via existing tool
                // Option 3: Queue for next conversation
            }
        }
    }
}

public sealed record DreamerSchedule
{
    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(30);
}
```

#### Pattern B: Manual invocation (Connect4 demo)

For simple applications without a host:

```csharp
// Between games in Connect4 — "the agent sleeps between games"
if (stats.TotalGames % 3 == 0)  // dream every 3 games
{
    display.SetStatus("💤 Agent is dreaming...");
    var dreamResult = await dreamer.DreamAsync();

    foreach (var discovery in dreamResult.Discoveries)
        await machine.SignalInsightAsync($"💭 Dream: {discovery}");

    display.SetStatus(
        $"💤 Dreamed: {dreamResult.Decayed} forgotten, " +
        $"{dreamResult.Reinforced} reinforced, " +
        $"{dreamResult.Discoveries.Count} discoveries");
}
```

#### Pattern C: `ProducerConsumer<T>` (existing pattern)

For applications already using Ananke's worker pattern:

```csharp
// DreamTrigger is queued when entry count exceeds threshold
// or when a high-surprise entry is committed
public sealed class DreamWorker(IDreamer dreamer) : IBackgroundWorker<DreamTrigger>
{
    public async Task HandleAsync(DreamTrigger trigger, CancellationToken ct) =>
        await dreamer.DreamAsync(ct);
}
```

### Dependency graph

```
IDreamer
  ├── IEmpiricalMemory (read entries, reinforce, contradict)
  ├── IEmbeddingModel (form prediction vectors, compute similarity)
  ├── IKnowledgeStore (optional — search for evidence during curiosity walk)
  ├── ISimulationSource (optional — imagined evidence via self-play / scenarios)
  ├── AffectOptions (decay parameters, learning rate)
  └── DreamerOptions (batch size, thresholds)
```

All dependencies except `ISimulationSource` already exist in the framework.
The dreamer introduces no new storage or transport abstractions.
`ISimulationSource` is domain-specific and provided by the application.

---

## Phase 4: Intrinsic Reward — Vector-Space Predictions

**Goal**: The dreamer forms predictions as vectors (not scalars), computes
surprise in embedding space, and derives reward from the surprise × coherence
matrix.

### Prediction formation

```
Entry: "ServiceA GC pause > 200ms → ServiceB timeout spike"
       (Kind = Pattern, Confidence = 0.6, Variance = 0.4)

Step 1: Recall similar entries
  → "Memory leak in ServiceA → downstream queue backup" (conf 0.8)
  → "ServiceC CPU spike → ServiceB latency" (conf 0.5)

Step 2: Form predicted vector
  predicted = weightedAvg(
      embed("GC pause causes timeout"),        weight = 0.6 (self confidence)
      embed("memory leak causes queue backup"), weight = 0.8
      embed("CPU spike causes latency"),        weight = 0.5
  )
  This represents: "I expect the evidence around this entry to look like
   general resource-pressure-causes-downstream-impact patterns"

Step 3: Actually search
  Search IKnowledgeStore for "ServiceA GC pause ServiceB timeout"
  → Returns log excerpts, incident reports, etc.
  actual = centroid of found evidence embeddings

Step 4: Compute surprise
  surprise = 1 - cosine(predicted, actual)

Step 5: Compute coherence
  coherence = avg(cosine(actual, each neighbor embedding))
  → High if the found evidence fits the neighborhood
  → Low if it's an outlier
```

For **cold start** (no similar entries):

```
predicted = entry's own embedding + gaussian noise (σ = 0.1)
→ Guarantees moderate surprise on first exploration
→ Any real evidence is more informative than noise
```

### Why this works for false belief detection

A false pattern like "deploy on Friday → Monday outages" would:

1. Predict: evidence should look like deployment-timing correlations
2. Actually find: Monday outages have many causes (weekend batch jobs, cache
   expiration, etc.) — evidence centroid is diffuse
3. Surprise: moderate (prediction was specific, reality is diffuse)
4. Coherence: low (found evidence doesn't cluster around the deployment
   hypothesis — it scatters across multiple causes)
5. Intrinsic reward: **negative** (surprising + incoherent → noise)
6. Entry strength decreases, variance increases, confidence drops

After several exploration cycles, the false pattern decays below threshold
and is removed. No human needed.

---

## Phase 5: Qdrant Integration

**Goal**: `QdrantEmpiricalMemory` supports all signal fields, `BrowseAsync`,
and the prediction-error reinforcement path.

### Schema additions

New payload fields (added to collection creation):

```csharp
await _client.CreatePayloadIndexAsync(_collection, StrengthKey,
    PayloadSchemaType.Float, ct: ct);
// valence, intensity: stored but not indexed (used in client-side scoring)
// variance, last_prediction_error: stored, not indexed
```

### `BrowseAsync` implementation

Uses Qdrant's `ScrollAsync` with optional filter on `kind`:

```csharp
public async Task<IReadOnlyList<EmpiricalEntry>> BrowseAsync(
    int offset, int limit, EmpiricalKind? kind, CancellationToken ct)
{
    Filter? filter = kind is not null
        ? new Filter { Must = { Conditions.MatchKeyword(KindKey, kind.ToString()!.ToLower()) } }
        : null;

    var result = await _client.ScrollAsync(_collection,
        filter: filter,
        limit: (uint)limit,
        offset: new PointId { Num = (ulong)offset },
        payloadSelector: true,
        cancellationToken: ct);

    return result.Select(MapPoint).ToList();
}
```

### `DecayAsync` via `SetPayloadAsync`

The dreamer's decay sweep updates strength in bulk without re-embedding:

```csharp
// For each entry browsed:
await _client.SetPayloadAsync(_collection,
    new Dictionary<string, Value>
    {
        [StrengthKey] = newStrength,
        [ConfidenceKey] = newConfidence  // if variance-derived
    },
    [new PointId { Uuid = entryId }],
    cancellationToken: ct);

// For entries below threshold:
await _client.DeleteAsync(_collection,
    [new PointId { Uuid = entryId }],
    cancellationToken: ct);
```

---

## Phase 6: Consolidation (Deferred)

**Goal**: Promote stable, well-confirmed empirical entries into
`IKnowledgeStore` as immutable semantic knowledge. The dreamer identifies
candidates during its cycle.

### Criteria

```csharp
bool ShouldConsolidate(EmpiricalEntry entry) =>
    entry.Strength > 0.8f &&
    entry.Variance < 0.05f &&
    entry.ObservationCount > 10 &&
    entry.Kind is EmpiricalKind.Pattern or EmpiricalKind.Heuristic;
```

### Process

1. Dreamer finds candidate during browse
2. Generates a `KnowledgeDocument` from the entry:
   - Text: structured description of the pattern/heuristic
   - Metadata: source entries, evidence links, confidence at promotion time
3. Upserts into `IKnowledgeStore`
4. Marks the empirical entry: `AbstractedInto = knowledgeDocId`
5. Entry excluded from future recall (but not deleted — audit trail)

### Why deferred

- Requires deciding what the promoted `KnowledgeDocument` text looks like
- May benefit from LLM summarization to generate readable text
- The signal model (phases 0–4) must be validated first
- The dreamer framework (phase 3) must exist first

---

## Summary: Dependency Graph Across Phases

```
Phase 0: Signal fields on EmpiricalEntry + Reinforcement
    │
    ├──► Phase 1: AffectOptions + prediction-error ReinforceAsync
    │       │
    │       ├──► Phase 2: Priority boost in RecallAsync
    │       │
    │       └──► Phase 3: IDreamer + InMemoryDreamer
    │               │         (decay + curiosity walk)
    │               │
    │               └──► Phase 4: Vector-space predictions
    │                       │     (intrinsic reward)
    │                       │
    │                       └──► Phase 6: Consolidation (deferred)
    │
    └──► Phase 5: Qdrant schema + QdrantEmpiricalMemory updates
            (can proceed in parallel with phases 1–4)
```

### Files touched per phase

| Phase | New files | Modified files |
|---|---|---|
| **P0** | — | `EmpiricalTypes.cs`, `InMemoryEmpiricalMemory.cs`, `QdrantEmpiricalMemory.cs`, tests |
| **P1** | — | `EmpiricalTypes.cs` (+`AffectOptions`), `InMemoryEmpiricalMemory.cs`, `QdrantEmpiricalMemory.cs`, tests |
| **P2** | — | `InMemoryEmpiricalMemory.cs`, `QdrantEmpiricalMemory.cs`, tests |
| **P3** | `IDreamer.cs`, `InMemoryDreamer.cs`, `ISimulationSource.cs` | `IEmpiricalMemory.cs` (+`BrowseAsync`), `InMemoryEmpiricalMemory.cs`, `QdrantEmpiricalMemory.cs`, tests |
| **P4** | — | `InMemoryDreamer.cs`, tests |
| **P5** | — | `QdrantEmpiricalMemory.cs`, tests |
| **P6** | — | `InMemoryDreamer.cs`, tests |

### Demo integration

The Connect4 demo is the natural first integration point:

| Phase | Demo change |
|---|---|
| **P0** | None — default values, existing behavior |
| **P1** | `GameAnalyzer` provides `Reward` on reinforcements (+1 win, -0.5 loss) |
| **P2** | Agent recalls with priority boost — critical patterns surface higher |
| **P3** | "Dream between games" — decay old patterns, explore connections. Optionally wire `Connect4Simulator : ISimulationSource` for self-play imagination |
| **P4** | Dreamer forms predictions about game patterns, tests against game history |
| **P5** | N/A (Connect4 uses in-memory) |
| **P6** | Mature patterns promoted to knowledge store (would need knowledge store added to demo) |
