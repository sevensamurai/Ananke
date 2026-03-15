# 15a — Empirical Memory Tuning Guide

Fine-tuning `AffectOptions`, `OfflineLearnerOptions`, and related parameters
for different domains. The defaults work well for general-purpose agents;
this guide explains when and how to adjust them.

**Prerequisite:** [Guide 15 — Empirical Memory](15-empirical-memory.md)

---

## Quick-reference: all configurable parameters

### `AffectOptions` — controls reinforcement, contradiction, decay, and recall

| Parameter | Default | What it controls |
|---|---|---|
| `LearningRate` | `0.1` | Strength delta per reinforcement. Higher = faster convergence, more noise sensitivity |
| `VarianceSmoothingFactor` | `0.1` | EMA α for variance tracking. Higher = recent errors dominate |
| `ReinforcementCooldownHours` | `1.0` | Min hours between full-effect reinforcements per entry |
| `DefaultConfidenceAdjustment` | `0.1` | Flat-path confidence bump when no `Reward` is provided |
| `InitialCommitConfidence` | `0.5` | Confidence assigned to agent-committed entries |
| `BaseDecayRate` | `0.98` | Per-cycle multiplicative strength decay |
| `VarianceDecayRate` | `0.02` | Variance-amplified decay multiplier |
| `DeletionThreshold` | `0.05` | Strength below which entries are removed |
| `ContradictionStrengthMultiplier` | `2.0` | Multiplier on `LearningRate` for contradiction strength penalty |
| `ContradictionValenceShift` | `−0.5` | How far valence shifts toward negative on contradiction |
| `ContradictionIntensityShift` | `0.3` | How much surprise intensity increases on contradiction |
| `ContradictionConfidencePenalty` | `0.3` | Flat-path confidence reduction on contradiction |
| `MaxPriorityBoost` | `0.3` | Max recall priority boost from valence × intensity |
| `MaxEvidenceCount` | `50` | Cap on retained evidence entries per entry |

### `OfflineLearnerOptions` — controls background learning cycles

| Parameter | Default | What it controls |
|---|---|---|
| `ExplorationBatchSize` | `5` | Entries explored per curiosity walk |
| `CuriosityThreshold` | `0.5` | Prediction-error/variance threshold for curious selection |
| `ExplorationRandomFraction` | `0.2` | ε-greedy fraction reserved for random exploration |
| `DiscoveryThreshold` | `0.3` | Minimum reward to count as a reportable discovery |
| `ConfirmationWeight` | `0.3` | Intrinsic reward coefficient for expected + coherent |
| `NoisePenaltyWeight` | `−0.3` | Intrinsic reward coefficient for surprising + incoherent |
| `ContradictionPenaltyWeight` | `−0.5` | Intrinsic reward coefficient for expected + incoherent |
| `CoherenceNeutral` | `0.5` | Coherence value when no neighbors exist |
| `ExplorationContradictionThreshold` | `−0.1` | Reward below which exploration triggers contradiction |
| `SelfPredictionScale` | `0.5` | Weight of self-prediction when no external evidence |
| `ReflectiveEvidenceWeight` | `1.0` | Weight of real-data evidence (vs simulated) |
| `SimulationEvidenceWeight` | `0.3` | Weight of simulated evidence relative to reflective |
| `SimulationMinConfidence` | `0.2` | Min confidence before simulation is attempted |
| `ConsolidationMinStrength` | `0.8` | Min strength for consolidation eligibility |
| `ConsolidationMaxVariance` | `0.05` | Max variance for consolidation eligibility |
| `ConsolidationMinObservations` | `10` | Min observations for consolidation eligibility |

### Constructor-level parameters

| Parameter | Default | Location |
|---|---|---|
| `dedupThreshold` | `0.9` | `InMemoryEmpiricalMemory` / `QdrantEmpiricalMemory` constructor |
| `TimeDecayOptions.HalfLifeDays` | `90` | Recency weighting half-life |
| `TimeDecayOptions.FloorWeight` | `0.3` | Minimum recency weight |

---

## Recipes by domain

### Game agent (Connect4, chess, etc.)

Fast learning with aggressive contradiction. Games generate high-frequency,
unambiguous win/loss signals.

```csharp
var affect = new AffectOptions
{
    LearningRate = 0.15f,                    // faster convergence — games have clear signals
    ReinforcementCooldownHours = 0.001f,     // near-zero — multiple games per second
    ContradictionStrengthMultiplier = 3.0f,  // punish losing moves harder
    ContradictionValenceShift = -0.7f,       // strong negative valence on contradiction
    MaxPriorityBoost = 0.5f,                 // let surprising outcomes dominate recall
    MaxEvidenceCount = 20,                   // games are short — less evidence needed
    InitialCommitConfidence = 0.3f,          // start skeptical — let wins prove the pattern
};

var memory = new InMemoryEmpiricalMemory(embedder,
    dedupThreshold: 0.85f,                   // slightly looser merge — similar positions vary
    affectOptions: affect);

var offlineOptions = new OfflineLearnerOptions
{
    ExplorationBatchSize = 10,               // explore more per cycle — games accumulate fast
    CuriosityThreshold = 0.3f,              // lower threshold — explore more broadly
    DiscoveryThreshold = 0.5f,              // only report strong discoveries
    ContradictionPenaltyWeight = -0.7f,     // penalize contradictions harder
    ConsolidationMinObservations = 20,       // need more games before promoting
    Affect = affect
};
```

### Incident analysis / ops agent

Slow, cautious learning. Signals are ambiguous, stakes are high, and
entries may not be validated for days or weeks.

```csharp
var affect = new AffectOptions
{
    LearningRate = 0.05f,                    // slow — incidents are rare and ambiguous
    ReinforcementCooldownHours = 4.0f,       // prevent burst-reinforcement from alert storms
    ContradictionStrengthMultiplier = 1.5f,  // gentler — contradictions may be false negatives
    ContradictionValenceShift = -0.3f,       // moderate negative valence
    ContradictionConfidencePenalty = 0.15f,  // don't destroy confidence on a single contradiction
    MaxPriorityBoost = 0.2f,                 // surprise matters less — recency and confidence dominate
    MaxEvidenceCount = 100,                  // long investigation threads
    InitialCommitConfidence = 0.5f,          // start neutral
};

var memory = new QdrantEmpiricalMemory(client, embeddingModel,
    dedupThreshold: 0.92f,                   // strict dedup — similar incidents are often distinct
    affectOptions: affect);

var offlineOptions = new OfflineLearnerOptions
{
    ExplorationBatchSize = 3,                // small — compute budget matters
    CuriosityThreshold = 0.7f,              // only explore very surprising entries
    ExplorationRandomFraction = 0.3f,       // more random — don't tunnel-vision on recent surprises
    DiscoveryThreshold = 0.2f,              // report even modest discoveries
    NoisePenaltyWeight = -0.1f,             // lenient with incoherent surprises — ops is noisy
    ConsolidationMinObservations = 5,        // incidents are rare — consolidate sooner
    ConsolidationMinStrength = 0.7f,         // lower bar — high-strength incidents are rare
    Affect = affect
};
```

### Coding assistant / knowledge worker

Balanced learning. Feedback is semi-structured (human confirms or corrects),
cycle times are hours to days.

```csharp
var affect = new AffectOptions
{
    LearningRate = 0.1f,                     // default — moderate pace
    ReinforcementCooldownHours = 1.0f,       // prevent back-to-back reinforcements in one session
    ContradictionStrengthMultiplier = 2.0f,  // default punishment
    MaxPriorityBoost = 0.3f,                 // default — balanced recall
    MaxEvidenceCount = 50,                   // default
    InitialCommitConfidence = 0.5f,          // default — trust the agent's initial judgment
};

var memory = new QdrantEmpiricalMemory(client, embeddingModel,
    affectOptions: affect);

var offlineOptions = new OfflineLearnerOptions
{
    ExplorationBatchSize = 5,                // default
    ConfirmationWeight = 0.4f,              // slightly reward confirmation more — code patterns are stable
    SelfPredictionScale = 0.3f,             // discount self-prediction — external validation matters
    ConsolidationMinObservations = 10,       // default
    Affect = affect
};
```

---

## Tuning strategy

### 1. Start with defaults, observe metrics

The defaults (`AffectOptions` / `OfflineLearnerOptions` with no arguments)
work for most use cases. Monitor these metrics first (see
[Guide 15 — Observability](15-empirical-memory.md#observability--monitoring-whether-learning-works)):

| Metric | Healthy range | What to tune if off |
|---|---|---|
| Hit rate (`recall_hits / recalls`) | > 0.3 | `dedupThreshold` (too tight → duplicates), `MinConfidence` (too high → filtering good entries) |
| Dedup rate | 0.1–0.5 | `dedupThreshold` (too loose → over-merging, too tight → bloat) |
| Contradiction rate | > 0, < 0.3 of commits | `ContradictionStrengthMultiplier`, `ContradictionConfidencePenalty` |

### 2. Tune the reinforcement loop

The core learning dynamic is **prediction error**:

```
predictionError = |confidence − reward|
strengthDelta   = LearningRate × (1 − predictionError) × cooldown
variance        = EMA(predictionError²)
confidence      = 1 / (1 + variance)
```

- **Learning too slow?** Increase `LearningRate`, decrease `ReinforcementCooldownHours`
- **Overreacting to noise?** Decrease `LearningRate`, increase `VarianceSmoothingFactor`
- **Entries dying too fast?** Decrease `BaseDecayRate` toward 1.0, decrease `VarianceDecayRate`
- **Zombies never die?** Increase `VarianceDecayRate`, lower `DeletionThreshold`

### 3. Tune contradiction severity

Contradiction is modeled as maximally surprising negative evidence
(prediction error = 1.0). The severity knobs are:

| Knob | Effect when increased |
|---|---|
| `ContradictionStrengthMultiplier` | Entry loses strength faster → removed sooner by decay |
| `ContradictionValenceShift` | Stronger negative valence → entry deprioritized in recall |
| `ContradictionIntensityShift` | Higher surprise → entry gets *more* recall priority boost (intentional: surprising entries are worth re-examining) |
| `ContradictionConfidencePenalty` | Direct confidence reduction (flat path only) |

**Rule of thumb:** In domains where contradictions are definitive (games, tests),
crank `ContradictionStrengthMultiplier` up (2.5–4.0). In domains where
contradictions are ambiguous (ops, medical), keep it low (1.0–2.0).

### 4. Tune offline learning

The intrinsic reward matrix controls what the curiosity walk rewards:

```
discovery   = surprise × coherence                    (positive)
confirmation = (1−surprise) × coherence × ConfirmationWeight
noise       = surprise × (1−coherence) × NoisePenaltyWeight
contradiction = (1−surprise) × (1−coherence) × ContradictionPenaltyWeight
```

- **Too many false discoveries?** Lower `DiscoveryThreshold`, increase `NoisePenaltyWeight` toward 0
- **Missing real discoveries?** Lower `CuriosityThreshold`, increase `ExplorationBatchSize`
- **Consolidating too early?** Increase `ConsolidationMinObservations` or `ConsolidationMinStrength`

---

## What to read next

- [Guide 15 — Empirical Memory](15-empirical-memory.md) — core concepts, API, backends
- [Guide 14 — Testing](14-testing.md) — use `InMemoryEmpiricalMemory` + `InMemoryEmbedder` for zero-dependency tests
- [Connect4Demo](../../src/demos/Connect4Demo/) — working example of game-agent tuning

← [Learning Path](../learning.md)
