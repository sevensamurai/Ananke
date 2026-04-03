# ADR-008: Affective Signals for Empirical Memory — Emotion-Analogous Learning Mechanics

| Field         | Value                                                              |
|---------------|--------------------------------------------------------------------|
| **Status**    | Proposed                                                           |
| **Date**      | 2025-07-28                                                         |
| **Authors**   | —                                                                  |
| **Deciders**  | Ananke maintainers                                                 |
| **Tags**      | empirical-memory, learning, reinforcement, affect, signals, decay  |
| **Relates to**| ADR-007 (background cognitive processes, empirical memory), `IEmpiricalMemory`, `EmpiricalEntry`, `InMemoryEmpiricalMemory` |

---

## Context

ADR-007 established `IEmpiricalMemory` as the third memory layer — a mutable
store for patterns, skills, and heuristics learned from agent-human
collaboration. The current reinforcement model is **flat**: `ReinforceAsync`
bumps confidence by a fixed amount (default `+0.1`), increments the
observation count, and updates recency. `ContradictAsync` subtracts a fixed
amount (`-0.3`). Recall ranking uses a composite score of
`vectorSimilarity × confidence × recencyWeight`.

This model works for simple cases but has structural limitations:

| Limitation | Consequence |
|---|---|
| **No prediction error** — reinforcement is the same magnitude regardless of whether the outcome was expected or surprising | Expected confirmations and surprising discoveries strengthen beliefs equally; no information-theoretic signal |
| **No outcome direction** — the system tracks *frequency of reinforcement* but not *whether outcomes were positive or negative* | A pattern that correlates with bad outcomes is reinforced the same as one with good outcomes |
| **No intensity signal** — high-stakes discoveries and trivial observations receive the same treatment | No mechanism to prioritize critical knowledge over noise |
| **No uncertainty tracking** — confidence is a single scalar with no history | Cannot distinguish "seen once, worked" from "seen 20 times, works 95% of the time" |
| **No forgetting** — entries persist indefinitely; only recency weighting in recall scoring provides time pressure | Stale or unstable beliefs accumulate, polluting recall results |
| **No abstraction** — episodic entries never graduate to higher-level semantic rules | The store grows linearly with experience; no compression or generalization |

### The proposal: affect-analogous signals

Drawing from computational neuroscience (Rescorla-Wagner learning rule,
temporal difference learning, somatic marker hypothesis), we propose enriching
empirical entries with **emotion-analogous signals** — not to simulate feelings,
but to decompose the learning signal into orthogonal dimensions that serve
distinct purposes:

| Signal | Analogy | Range | Purpose |
|---|---|---|---|
| **Valence** | Positive/negative feeling | `[-1.0, +1.0]` | Direction of outcome — was this good or bad? |
| **Excitement** | Arousal/intensity | `[0.0, 1.0]` | Magnitude of outcome — how much does this matter? |
| **Surprise** | Prediction error | `[0.0, 1.0]` | Difference between expected and actual outcome |

### The critical design constraint

> **Valence and excitement influence priority, not truth.**
> **Surprise and prediction error determine truth and reinforcement.**

This separation prevents **emotional self-reinforcement loops** — the
pathological case where high-valence entries get recalled more, get confirmed
more, and grow ever stronger regardless of their actual predictive accuracy.
In human cognition, this is the mechanism behind confirmation bias and
catastrophizing. An engineered system can avoid it by architecture.

### How the signals decompose

```
Outcome of applying knowledge
         │
         ├── What happened?
         │     ├── Valence:    positive or negative?     → affects PRIORITY
         │     └── Excitement: intense or mild?          → affects PRIORITY
         │
         └── Was it expected?
               ├── Surprise:   high prediction error?    → affects TRUTH
               └── Variance:   historically stable?      → affects CONFIDENCE
```

Priority determines **what surfaces** in recall (ranking, ordering).
Truth determines **what persists** in memory (reinforcement, decay, deletion).

---

## Analysis

### Strengths

#### 1. Clean separation of priority from truth

The current system conflates observation frequency with importance. A pattern
observed 50 times during routine operations dominates recall over a critical
pattern observed 3 times during incidents. The proposed decomposition lets the
system rank the 3-observation critical pattern higher (high excitement) while
maintaining that the 50-observation pattern is more epistemically certain
(low variance, high confidence).

```
Current:  compositeScore = vectorSimilarity × confidence × recency
Proposed: compositeScore = vectorSimilarity × confidence × recency
                           × priorityBoost(valence, excitement)
```

The `priorityBoost` is a recall-time multiplier that doesn't touch the stored
confidence — it only affects which results surface first. The confidence itself
is driven purely by prediction error history.

#### 2. Prediction-error-driven reinforcement is information-theoretically sound

The Rescorla-Wagner model from learning theory establishes that **learning rate
should be proportional to prediction error**. When the outcome matches the
prediction (low surprise), there's little new information — reinforcement
should be small. When the outcome is surprising (high prediction error), the
entry needs significant updating.

```
// Current: flat reinforcement
confidence += 0.1f;  // same regardless of whether outcome was expected

// Proposed: prediction-error-modulated reinforcement
strength += learningRate × (1 - predictionError);  // surprising → less strengthening of current belief
                                                     // expected → confirms current belief
```

This is well-grounded in both neuroscience and reinforcement learning theory
(TD-learning). It makes the system's learning rate adaptive rather than fixed.

#### 3. Variance tracking enables epistemic humility

Tracking a history of prediction errors and computing variance lets the system
distinguish:

| Scenario | Prediction error history | Variance | Confidence |
|---|---|---|---|
| Consistently confirmed pattern | `[0.05, 0.03, 0.04, 0.02]` | Low | High |
| Inconsistently observed pattern | `[0.8, 0.1, 0.6, 0.2]` | High | Low |
| Newly discovered, untested | `[0.7]` (single observation) | N/A (insufficient data) | Low (prior) |

The formula `confidence = 1 / (1 + variance)` is a principled Bayesian-flavored
estimate: as variance approaches 0, confidence approaches 1; as variance grows,
confidence shrinks toward 0.

#### 4. Forgetting prevents knowledge pollution

The current system has no active decay — entries persist indefinitely. The
proposed model ties decay to both time and stability:

```
strength *= 0.98f;                    // base time decay
strength -= variance * 0.02f;         // unstable beliefs decay faster
if (strength < threshold) delete();   // garbage collection
```

This means:
- **Strong, stable beliefs** persist almost indefinitely (tiny per-cycle decay)
- **Weak, unstable beliefs** decay quickly (variance amplifies decay)
- **Strong but volatile beliefs** decay moderately (strength buffers against variance)

This aligns with how human memory works: frequently confirmed, stable
knowledge becomes long-term memory; uncertain, rarely-used knowledge fades.

#### 5. Abstraction promotion creates a compression mechanism

The idea that entries meeting criteria (high strength, low variance, high
usage) should be promoted from episodic to semantic knowledge addresses the
unbounded growth problem. In the Ananke architecture, this maps to:

```
IEmpiricalMemory (mutable, empirical)
    │
    │  When: strength > threshold && variance < threshold && usage > threshold
    │
    ▼
IKnowledgeStore (immutable, semantic)
    Promoted as a structured KnowledgeDocument
```

This is analogous to memory consolidation in neuroscience: episodic memories
that are repeatedly accessed and consistently confirmed eventually become
semantic knowledge — facts that are "just known" without reference to specific
episodes.

### Weaknesses and Risks

#### 1. The reward signal problem (critical)

The pseudo-code assumes `float predictedOutcome` and `float actualOutcome` are
available. In an agentic system, this is the hardest unsolved problem:

- **What constitutes a "reward"?** When an agent applies a skill to investigate
  a timeout, what's the numeric outcome? "Found the root cause" is binary, not
  continuous. "User was satisfied" is subjective and delayed.
- **Who provides the prediction?** The agent must form an expectation *before*
  acting to compute surprise afterward. This requires either (a) an explicit
  prediction step injected into the agent loop, or (b) an implicit baseline
  derived from past outcomes for similar situations.
- **Temporal credit assignment** — if the agent follows a 5-step skill and
  the investigation succeeds at step 4, which steps get credit? The reward
  is sparse and delayed.

**Mitigation**: Define a minimal reward vocabulary:

| Signal | Source | Value |
|---|---|---|
| Human explicit confirmation | "Yes, that's the root cause" | `+1.0` |
| Human explicit rejection | "No, that's wrong" | `-1.0` |
| Tool success | Tool returned useful results | `+0.3` |
| Tool failure | Tool returned empty or error | `-0.3` |
| Implicit: conversation continued | User stayed engaged | `+0.1` |
| Implicit: conversation abandoned | User left or changed topic | `-0.1` |
| Background: pattern matched new data | Auto-detected confirmation | `+0.2` |

For the prediction baseline, use the entry's current confidence as the
predicted probability of success. This avoids requiring an explicit prediction
step:

```csharp
float predicted = entry.Confidence;  // "I expect this to work {confidence}% of the time"
float actual = reward;               // what actually happened
float surprise = MathF.Abs(predicted - actual);
```

This is a simplification but avoids the full prediction mechanism while
preserving the core information-theoretic benefit.

#### 2. Self-reinforcement loops are reduced but not eliminated

The design claims "valence and excitement influence priority, not truth." But
if valence affects recall ranking, high-valence entries get recalled more often.
More recall means more opportunities for reinforcement. More reinforcement
means higher confidence. Higher confidence means even higher composite scores.

```
High valence → higher priority → recalled more → reinforced more → higher confidence
                                                                         │
                                                                         ▼
                                                              (loop feeds itself)
```

The loop is weaker than if valence directly affected confidence, but it still
exists through the indirect path of recall frequency.

**Mitigation**: Cap the reinforcement rate. An entry that has been reinforced
within the last N hours gets a reduced or zero reinforcement increment. This
breaks the frequency-driven loop:

```csharp
float timeSinceLastReinforcement = (now - entry.LastObserved).TotalHours;
float cooldownFactor = Math.Min(1f, timeSinceLastReinforcement / cooldownHours);
float effectiveReinforcement = baseReinforcement * cooldownFactor;
```

Additionally, introduce a **diversity mechanism** in recall: after the top-K
results are computed, ensure at least one low-confidence / high-surprise entry
is included (an "exploration slot"), analogous to ε-greedy exploration in
reinforcement learning.

#### 3. Numerical stability and parameter sensitivity

The system introduces multiple floating-point parameters that interact:

- Learning rate for reinforcement (`0.1f`)
- Base decay rate (`0.98f`)
- Variance-amplified decay (`0.02f`)
- Deletion threshold (`0.05f`)
- Abstraction thresholds (strength `> 0.8`, variance `< 0.05`, usage `> 5`)

These are all magic numbers. Small changes can cause qualitatively different
behavior:
- Too-aggressive decay deletes useful uncertain knowledge
- Too-conservative decay never forgets anything (status quo)
- Wrong abstraction thresholds either promote too early (overgeneralizing) or
  never promote (no compression benefit)

**Mitigation**: Make all thresholds configurable via an `AffectOptions` record
with sensible defaults. Document the expected behavior at boundary values.
Plan for empirical tuning — the irony of needing empirical data to tune the
empirical memory system is noted but unavoidable.

#### 4. Prediction error history is unbounded

`mem.PredictionErrors.Add(predictionError)` grows without bound. For a
frequently-reinforced pattern, this list could contain thousands of entries.

**Mitigation**: Use a sliding window (e.g., last 50 prediction errors) or
an exponential moving average of variance instead of storing the full history:

```csharp
// Exponential moving average — O(1) storage
float alpha = 0.1f;  // smoothing factor
mem.Variance = (1 - alpha) * mem.Variance + alpha * MathF.Pow(predictionError, 2);
```

This is more memory-efficient and aligns with how the existing
`InMemoryEmpiricalMemory` caps evidence at 50 entries.

#### 5. Abstraction is a destructive one-way door

The proposed `ShouldAbstract` function promotes an entry and **deletes the
episodic cluster**:

```csharp
var cluster = RetrieveCluster(qdrant, mem);
var semantic = CreateSemanticRule(cluster);
qdrant.Upsert("memories", semantic);
foreach (var e in cluster) DeleteMemory(e);  // ← destructive
```

If the promoted semantic rule later turns out to be wrong, the supporting
evidence is gone. The system cannot "remember why it believed this."

**Mitigation**: Don't delete the source entries. Instead, mark them as
`AbstractedInto = semanticRuleId` and exclude them from normal recall (filter
by `abstraction_level == 0` by default). They remain available for audit,
debugging, or reversal. This trades storage for reversibility — the right
trade in a learning system.

#### 6. The "affect" metaphor may mislead

Calling these signals "emotions" or "affect" invites anthropomorphization.
Developers may expect richer behavior than what's implemented, or may resist
the framing as unscientific. The signals are really just **multi-dimensional
metadata for a reinforcement learning system**.

**Mitigation**: In the API and documentation, use technical terms:
`PredictionError` not "surprise," `OutcomeValence` not "emotion,"
`OutcomeIntensity` not "excitement." Reserve the affect metaphor for
high-level architecture discussions where the analogy aids intuition.

### Feasibility Assessment

| Component | Difficulty | Dependencies | Notes |
|---|---|---|---|
| Add signal fields to `EmpiricalEntry` | Low | None | New optional properties on existing record |
| Add signal fields to Qdrant payload schema | Low | `Ananke.Qdrant` | New indexed payload fields |
| Prediction-error-driven `ReinforceAsync` | Medium | Reward signal source | Core logic change; needs reward vocabulary |
| Variance tracking (EMA) | Low | None | Single float field, updated on reinforce |
| Decay sweep (background process) | Medium | ADR-007 Part 1 (background thinkers) | Scheduled task that iterates entries |
| Priority boost in recall scoring | Low | None | Multiply existing composite score |
| Abstraction promotion | High | `IKnowledgeStore`, clustering logic | Deferred — requires LLM-based summarization |
| Reward signal extraction from agent loop | High | Agent tool framework, conversation flow | Hardest part; needs integration points |
| Configurable `AffectOptions` | Low | None | Record with defaults |

### Comparison with Current Model

```
┌─────────────────────────────────────────────────────────────────────────┐
│                     Current vs. Proposed                                 │
│                                                                         │
│  CURRENT (ADR-007 P2)               PROPOSED (ADR-008)                  │
│  ─────────────────────               ──────────────────                  │
│                                                                         │
│  Reinforce:                          Reinforce:                          │
│    confidence += 0.1                   strength += lr × (1 - |error|)   │
│    count++                             variance = EMA(error²)            │
│    lastObserved = now                  confidence = 1 / (1 + variance)   │
│                                        count++                           │
│                                        lastObserved = now                │
│                                                                         │
│  Contradict:                         Contradict:                         │
│    confidence -= 0.3                   strength -= penalty               │
│                                        record prediction error = 1.0     │
│                                        variance recalculated             │
│                                                                         │
│  Recall ranking:                     Recall ranking:                     │
│    sim × confidence × recency          sim × confidence × recency        │
│                                        × priorityBoost(valence, excite)  │
│                                                                         │
│  Decay:                              Decay:                              │
│    (none — recency in recall only)     strength *= baseDecay             │
│                                        strength -= variance × varDecay   │
│                                        delete if strength < threshold    │
│                                                                         │
│  Forgetting:                         Forgetting:                         │
│    (none)                              active decay + deletion            │
│                                                                         │
│  Abstraction:                        Abstraction:                        │
│    (none)                              promote to IKnowledgeStore         │
│                                        when strong + stable + used       │
│                                                                         │
│  Uncertainty:                        Uncertainty:                        │
│    (none — confidence is a guess)      variance of prediction errors     │
│                                        confidence derived from variance  │
│                                                                         │
│  Outcome direction:                  Outcome direction:                  │
│    (none)                              valence: positive/negative         │
│                                        excitement: intensity              │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Decision

**Adopt the affective signal model as an evolution of empirical memory
reinforcement**, with the following scoping decisions:

### What we adopt

1. **Prediction-error-driven reinforcement** — replace flat `+0.1` confidence
   bumps with prediction-error-modulated strength updates. This is the core
   information-theoretic improvement.

2. **Variance tracking via EMA** — add a `Variance` field (exponential moving
   average of squared prediction errors) and derive `Confidence` from it:
   `confidence = 1 / (1 + variance)`. This replaces the current manually-bumped
   confidence scalar.

3. **Outcome signals as metadata** — add `Valence` and `Intensity` (renamed
   from "excitement" for API clarity) as optional fields on `EmpiricalEntry`.
   These are **stored but not used in the truth path** — they influence recall
   priority only.

4. **Active decay** — implement a decay sweep as a background process
   (aligned with ADR-007 background thinkers). Entries below a strength
   threshold are soft-deleted (marked, not destroyed).

5. **Configurable parameters** — all thresholds, rates, and decay constants
   are encapsulated in an `AffectOptions` record with documented defaults.

### What we defer

1. **Abstraction promotion** — the mechanism to promote stable empirical entries
   into `IKnowledgeStore` requires LLM-based summarization and clustering. This
   is architecturally sound but complex. Defer to a future ADR once the base
   signal model is validated.

2. **Automatic reward extraction** — the full pipeline of extracting reward
   signals from agent tool outcomes, human confirmations, and conversation
   flow. Start with explicit reward via `ReinforceAsync` and `ContradictAsync`
   (callers provide the signal), then build implicit extraction incrementally.

3. **Exploration slot in recall** — the ε-greedy diversity mechanism in recall
   results. Valuable but can be added as a `RecallOptions` flag without
   changing the signal model.

4. **Simulated evidence (`ISimulationSource`)** — an optional interface that
   lets the offline learner generate imagined evidence by running domain-specific
   scenarios (self-play for games, Monte Carlo rollouts for planning). The
   framework defines the contract; the application provides the domain logic.
   Simulation evidence is always weighted below real data
   (`SimulationEvidenceWeight` defaults to 0.3). See the implementation plan
   for the full interface, evidence combination formula, and a concrete
   Connect4 self-play example.

### API naming

Use technical terms in the API, affect metaphor in architecture docs:

| Concept | API name | Architecture discussion |
|---|---|---|
| Surprise | `PredictionError` | "surprise signal" |
| Excitement | `Intensity` | "excitement / arousal" |
| Valence | `Valence` | "valence" (already technical) |
| Strength | `Strength` | "belief strength" |
| Forgetting | `DecayAsync` / `AffectOptions.BaseDecayRate` | "forgetting" |

---

## Proposed Changes

### New and modified types in `Ananke.Orchestration.Memory`

#### `EmpiricalEntry` — new optional signal fields

```csharp
// New fields on EmpiricalEntry (additive, non-breaking)

/// <summary>
/// Belief strength — driven by prediction-error-modulated reinforcement.
/// Decays over time; entries below the configured threshold are candidates
/// for removal. Distinct from <see cref="Confidence"/>, which is derived
/// from prediction error variance.
/// </summary>
public float Strength { get; init; }

/// <summary>
/// Outcome direction: -1.0 (negative outcome) to +1.0 (positive outcome).
/// Influences recall priority, not truth. Updated on reinforcement.
/// </summary>
public float Valence { get; init; }

/// <summary>
/// Outcome intensity: 0.0 (trivial) to 1.0 (critical).
/// Influences recall priority, not truth. Updated on reinforcement.
/// </summary>
public float Intensity { get; init; }

/// <summary>
/// Exponential moving average of squared prediction errors.
/// Used to derive <see cref="Confidence"/>: <c>1 / (1 + Variance)</c>.
/// Lower variance indicates a more stable, reliable belief.
/// </summary>
public float Variance { get; init; } = 1.0f;

/// <summary>
/// Most recent prediction error (|predicted - actual|).
/// Stored for diagnostics and reinforcement-cooldown logic.
/// </summary>
public float LastPredictionError { get; init; }
```

#### `AffectOptions` — configurable parameters

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
    /// Smoothing factor for exponential moving average of variance.
    /// Higher values weight recent errors more heavily. Range (0, 1). Default: 0.1.
    /// </summary>
    public float VarianceSmoothingFactor { get; init; } = 0.1f;

    /// <summary>Per-cycle multiplicative decay applied to strength. Default: 0.98.</summary>
    public float BaseDecayRate { get; init; } = 0.98f;

    /// <summary>
    /// Multiplier for variance-amplified decay:
    /// <c>strength -= Variance × VarianceDecayRate</c>.
    /// Unstable beliefs decay faster. Default: 0.02.
    /// </summary>
    public float VarianceDecayRate { get; init; } = 0.02f;

    /// <summary>Strength below which an entry is a candidate for removal. Default: 0.05.</summary>
    public float DeletionThreshold { get; init; } = 0.05f;

    /// <summary>
    /// Maximum priority boost from valence and intensity signals.
    /// Applied as: <c>score × (1 + boost × intensity × |valence|)</c>.
    /// Default: 0.3 (up to 30% boost).
    /// </summary>
    public float MaxPriorityBoost { get; init; } = 0.3f;

    /// <summary>
    /// Minimum hours between reinforcements for the same entry before
    /// full reinforcement strength is applied. Prevents frequency-driven
    /// self-reinforcement loops. Default: 1.0.
    /// </summary>
    public float ReinforcementCooldownHours { get; init; } = 1.0f;
}
```

#### `Reinforcement` — extended with outcome signal

```csharp
// New optional fields on Reinforcement

/// <summary>
/// The actual outcome value for prediction error computation.
/// When provided, the implementation computes prediction error as
/// <c>|entry.Confidence - Reward|</c> and uses it to modulate
/// reinforcement strength and update variance.
/// When <see langword="null"/>, falls back to flat reinforcement
/// (backward-compatible behavior).
/// </summary>
public float? Reward { get; init; }
```

### Modified composite scoring in recall

```csharp
// Current
var compositeScore = vectorScore * entry.Confidence * recencyWeight;

// Proposed
var baseScore = vectorScore * entry.Confidence * recencyWeight;
var priorityBoost = 1f + options.MaxPriorityBoost * entry.Intensity * MathF.Abs(entry.Valence);
var compositeScore = baseScore * priorityBoost;
```

The priority boost is **multiplicative on the final score** and bounded by
`MaxPriorityBoost`. It cannot make a low-confidence entry outrank a
high-confidence one by more than the configured percentage. Truth (confidence
derived from variance) remains the dominant factor.

### Modified reinforcement logic

```csharp
// Current
var updated = stored.Entry with
{
    Confidence = Math.Min(1.0f, stored.Entry.Confidence + adjustment),
    ObservationCount = stored.Entry.ObservationCount + 1,
    LastObserved = DateTimeOffset.UtcNow,
    Evidence = TrimEvidence([.. stored.Entry.Evidence, .. reinforcement.NewEvidence])
};

// Proposed (when Reward is provided)
float predicted = stored.Entry.Confidence;
float actual = reinforcement.Reward.Value;
float predictionError = MathF.Abs(predicted - actual);

// Cooldown: reduce reinforcement if recently reinforced
float hoursSinceLast = (float)(DateTimeOffset.UtcNow - stored.Entry.LastObserved).TotalHours;
float cooldown = MathF.Min(1f, hoursSinceLast / options.ReinforcementCooldownHours);

// Truth-based reinforcement: confirming prediction strengthens, surprising weakens current strength
float strengthDelta = options.LearningRate * (1f - predictionError) * cooldown;

// Variance update: EMA of squared prediction errors
float newVariance = (1f - options.VarianceSmoothingFactor) * stored.Entry.Variance
                  + options.VarianceSmoothingFactor * predictionError * predictionError;

// Confidence derived from variance (epistemic certainty)
float newConfidence = 1f / (1f + newVariance);

// Outcome signals: stored for priority, do NOT affect truth
float newValence = MathF.Clamp(actual, -1f, 1f);
float newIntensity = MathF.Clamp(MathF.Abs(actual), 0f, 1f);

var updated = stored.Entry with
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
```

When `Reward` is `null`, the existing flat-reinforcement behavior is preserved
(backward compatibility).

### Decay sweep (background process)

```csharp
/// <summary>
/// Applies time-based and variance-based decay to all entries.
/// Intended to be called periodically by a background process.
/// </summary>
public Task<int> DecayAsync(CancellationToken ct = default)
{
    int removed = 0;
    lock (_writeLock)
    {
        foreach (var (id, stored) in _entries)
        {
            var decayed = stored.Entry with
            {
                Strength = stored.Entry.Strength * _affectOptions.BaseDecayRate
                         - stored.Entry.Variance * _affectOptions.VarianceDecayRate
            };

            if (decayed.Strength < _affectOptions.DeletionThreshold)
            {
                _entries.TryRemove(id, out _);
                removed++;
            }
            else
            {
                _entries[id] = new StoredEntry(decayed, stored.Embedding);
            }
        }
    }
    return Task.FromResult(removed);
}
```

This is exposed on the implementation class (not the interface) initially, and
invoked by a background thinker on a configurable schedule. If the pattern
proves useful across implementations, promote `DecayAsync` to the interface.

### Qdrant payload schema additions

```json
{
  "vectors": {
    "embedding": {
      "size": 1536,
      "distance": "Cosine"
    }
  },
  "payload_schema": {
    "kind":              "keyword",
    "confidence":        "float",
    "observation_count": "integer",
    "first_observed":    "integer",
    "last_observed":     "integer",
    "source":            "keyword",
    "tags":              "keyword[]",
    "evidence":          "string[]",

    "strength":               "float",
    "valence":                "float",
    "intensity":              "float",
    "variance":               "float",
    "last_prediction_error":  "float"
  }
}
```

New indexed fields: `strength` (for decay threshold queries), `valence` and
`intensity` (for priority-filtered recall). `variance` and
`last_prediction_error` are stored but not indexed (used in computation, not
filtering).

---

## Consequences

### Positive

- **Information-theoretically sound reinforcement** — learning rate adapts to
  prediction error rather than applying fixed increments; surprising outcomes
  trigger more learning
- **Epistemic uncertainty is first-class** — variance-derived confidence
  distinguishes "often confirmed" from "occasionally seen," enabling better
  recall ranking
- **Priority and truth are architecturally separated** — valence/intensity
  affect ranking, not confidence; reduces confirmation bias risk
- **Active forgetting prevents pollution** — stale or unstable beliefs are
  actively decayed and eventually removed, keeping the store clean
- **Backward compatible** — all new fields are optional with defaults; existing
  `ReinforceAsync` callers that don't provide `Reward` get the current flat
  behavior
- **Configurable** — all parameters are tunable via `AffectOptions`; no magic
  numbers baked into the implementation
- **Aligned with ADR-007 architecture** — decay sweep is a natural background
  thinker; reward signals can flow through `SignalInsightAsync`

### Negative

- **Increased surface area** — `EmpiricalEntry` gains 5 new fields; the mental
  model for the type becomes richer
- **Parameter tuning burden** — `AffectOptions` has 7 parameters; incorrect
  tuning can cause pathological behavior (too aggressive decay, too slow
  learning)
- **Reward signal bootstrapping** — the system doesn't improve until callers
  provide reward signals; requires integration work in the agent loop
- **Complexity budget** — the empirical memory system is already the most
  complex memory layer; this adds another dimension of complexity

### Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| Parameters tuned for one domain (incident analysis) fail in another (game playing) | High | Configurable `AffectOptions`; document per-domain tuning guidance |
| Decay sweep deletes entries that were valuable but infrequently accessed | Medium | Soft-delete (mark, don't destroy); allow recovery within a grace period |
| Reward signal is absent in most early usage → system falls back to flat reinforcement everywhere | High | Acceptable — flat reinforcement is the current behavior; affect signals are incremental improvement |
| Valence-driven priority boost creates subtle recall bias over time | Medium | Cap boost via `MaxPriorityBoost`; reinforcement cooldown; monitor with existing OpenTelemetry counters |
| Numerical precision issues with EMA variance across very many updates | Low | Use `double` for variance computation internally; store as `float` |

---

## Implementation Timeline

| Phase | Scope | Changes | Depends on |
|---|---|---|---|
| **P0** | Add signal fields to `EmpiricalEntry` | Additive optional properties | None |
| **P1** | Add `AffectOptions` and prediction-error-driven `ReinforceAsync` | Logic change in `InMemoryEmpiricalMemory` | P0 |
| **P2** | Priority boost in recall scoring | Modified composite score formula | P0 |
| **P3** | `IOfflineLearner` + `ISimulationSource` + decay sweep | Offline learner service, optional simulation plug-in, `BrowseAsync` on `IEmpiricalMemory` | P1, ADR-007 P1 |
| **P4** | Qdrant schema + `QdrantEmpiricalMemory` signal support | Payload field additions, `SetPayloadAsync` for decay | P0 |
| **P5** | Reward signal extraction from agent tool outcomes | Integration with agent loop | P1 |
| **P6 — Later** | Abstraction promotion pipeline | LLM-based cluster → semantic rule | P3, deferred |

---

## References

- ADR-007: Background cognitive processes — establishes `IEmpiricalMemory`,
  `SignalInsightAsync`, and the background thinker architecture
- Rescorla, R.A. & Wagner, A.R. (1972) — prediction-error-driven learning
  model; foundational to the reinforcement approach proposed here
- Temporal Difference Learning (Sutton, 1988) — reinforcement learning with
  prediction error signals; the `strength += lr × (1 - |error|)` formula
  is a simplified TD update
- Damasio, A. (1994) "Descartes' Error" — somatic marker hypothesis; the
  inspiration for using affect-like signals to influence priority without
  determining truth
- Ananke `InMemoryEmpiricalMemory` — current flat reinforcement implementation
  that this ADR proposes to evolve
- Ananke `CatalogAwareKnowledgeStore.TimeDecay` — prior art for
  recency-based scoring; the decay sweep extends this from recall-time
  weighting to active storage management
- Ananke `AffectOptions` pattern follows `TimeDecayOptions` — configurable
  record with sensible defaults, same DX pattern
