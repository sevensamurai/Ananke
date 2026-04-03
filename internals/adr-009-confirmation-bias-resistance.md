# ADR-009: Confirmation Bias Resistance in Empirical Memory

| Field         | Value                                                              |
|---------------|--------------------------------------------------------------------|
| **Status**    | Accepted                                                           |
| **Date**      | 2025-07-28                                                         |
| **Authors**   | —                                                                  |
| **Deciders**  | Ananke maintainers                                                 |
| **Tags**      | empirical-memory, bias, reinforcement, learning, anti-lock-in      |
| **Supersedes**| Refines ADR-008 §2 (self-reinforcement loops) and §6 (affect metaphor) |
| **Relates to**| ADR-008 (affective signals), ADR-007 (background cognitive processes) |

---

## Context

ADR-008 introduced affective signals (Strength, Variance, Valence, Intensity,
prediction-error reinforcement) to the empirical memory system. The
implementation review revealed five vectors through which **confirmation bias
can enter and lock in beliefs** — the exact failure mode ADR-008 §2 warned
about but whose mitigations were incomplete.

### The goal

The empirical memory system must ensure that **evidence prevails over
resonance**. A belief that "feels right" (high valence, frequently recalled)
must not accumulate epistemic authority faster than a belief backed by
diverse, independently confirmed evidence. The system must resist lock-in
around preconceived theories.

### Five bias vectors identified

| # | Vector | Mechanism | Risk |
|---|---|---|---|
| 1 | **Recall → Reinforce loop** | High confidence → higher recall score → more use → more reinforcement → even higher confidence | Critical |
| 2 | **Flat reinforcement path** | `reinforce_empirical` tool calls `ReinforceAsync` without `Reward` → flat +0.1 confidence with no prediction-error discipline | High |
| 3 | **Dedup merge confidence bump** | `CommitAsync` dedup adds +0.1 confidence on merge — pure repetition becomes confidence | Medium |
| 4 | **Valence/Intensity derivation** | Valence/Intensity derived from raw outcome (`actual`) not surprise (`actual - predicted`) — outcome quality drives recall priority instead of informational value | Medium |
| 5 | **ContradictAsync ignores affect** | Contradiction subtracts flat -0.3 from Confidence but doesn't touch Strength, Variance, or Valence — contradicted entries resist decay and retain priority | Medium |

### Existing safeguards that were working

- Cooldown on reinforcement (limits frequency-driven loops)
- Variance → Confidence derivation (`1/(1+V)`)
- Curiosity walk with ε-greedy exploration
- Dedup similarity threshold at 0.9

### What was insufficient

- Cooldown gates **frequency** but not the **loop itself** — the feedback path
  from recall priority to reinforcement opportunity remains open
- The flat reinforcement path bypasses all prediction-error discipline
- No mechanism distinguishes "confirmed by evidence" from "repeated by habit"

---

## Decision

### Design principles adopted

1. **Evidence over repetition** — Observation count and source diversity
   matter; raw call frequency does not. Confidence comes from variance
   (prediction error history), not from accumulation of flat +0.1 bumps.

2. **Disconfirmation asymmetry** — Contradicting evidence has stronger
   effect than confirming evidence. A single clean disconfirmation weakens
   Strength by `2 × LearningRate`, records maximum prediction error (1.0),
   and increases Variance — compared to confirmation which strengthens by
   `LearningRate × (1 - error) × cooldown`.

3. **No undisciplined reinforcement when affect is active** — When the
   system has `AffectOptions`, the prediction-error path is the disciplined
   default. The flat path remains for backward compatibility (no
   `AffectOptions`) but is not the primary learning mechanism.

4. **Priority reflects informational value, not outcome direction** —
   Valence tracks surprise direction (better or worse than expected),
   Intensity tracks surprise magnitude (how unexpected). An entry with
   a negative surprise is just as likely to surface as one with a positive
   surprise — both are informative.

5. **Decay must be effective** — The decay sweep must actually persist
   Strength changes, not just compute them and discard.

### Changes implemented

#### Fix 1: Valence/Intensity derived from surprise, not outcome

**Before:**
```csharp
float newValence = Math.Clamp(actual, -1f, 1f);           // raw outcome
float newIntensity = Math.Clamp(MathF.Abs(actual), 0f, 1f); // outcome magnitude
```

**After:**
```csharp
float newValence = Math.Clamp(actual - predicted, -1f, 1f);  // surprise direction
float newIntensity = Math.Clamp(predictionError, 0f, 1f);     // surprise magnitude
```

**Why:** Valence now means "was the outcome better or worse than expected?"
and Intensity means "how unexpected was it?" An expected positive outcome
(predicted 0.8, actual 0.8) has Valence ≈ 0 and Intensity ≈ 0 — it's
not informative, so it doesn't boost recall priority. A surprising negative
outcome (predicted 0.8, actual 0.2) has Valence = -0.6 and Intensity = 0.6
— it's highly informative and surfaces in recall.

This breaks the feedback loop where positive outcomes → higher priority →
more use → more positive outcomes.

#### Fix 2: Decay writes Strength back

**Bug:** `InMemoryOfflineLearner.DecayAsync` computed `newStrength` but
called `ReinforceAsync` without `Reward`, which hit the flat path —
and the flat path never touched `Strength`. Decay was a no-op for
surviving entries.

**Fix:** Added `StrengthAdjustment` to the `Reinforcement` record.
When provided, the flat path applies the delta to `Strength`:

```csharp
// Reinforcement record — new field
public float? StrengthAdjustment { get; init; }

// Offline learner — decay sweep
await _memory.ReinforceAsync(entry.Id, new Reinforcement
{
    StrengthAdjustment = newStrength - entry.Strength,  // negative delta
    ConfidenceAdjustment = 0f,
    ...
});
```

#### Fix 3: ContradictAsync updates affective fields

**Before:** Flat -0.3 to Confidence. Strength, Variance, Valence,
Intensity unchanged.

**After (when AffectOptions active):**
- Records prediction error = 1.0 (maximum surprise)
- Updates Variance via EMA (increases uncertainty)
- Derives Confidence from new Variance
- Weakens Strength by `2 × LearningRate`
- Shifts Valence negative, increases Intensity

This ensures contradicted entries decay faster (higher Variance amplifies
decay), have lower confidence, and are more likely to be explored by the
curiosity walk (high prediction error).

#### Fix 4: Dedup merge stops bumping confidence

**Before:** `CommitAsync` dedup added +0.1 to Confidence on merge.

**After:** Dedup increments `ObservationCount` and merges `Evidence` but
does not touch `Confidence`. Confidence changes only through the
prediction-error path (`ReinforceAsync` with `Reward`) or contradiction.

This prevents repetition from masquerading as evidence. Ten independent
commits of the same pattern increase observation count to 10 but leave
confidence at the initial value until the pattern is actually tested
against reality.

#### Fix 5: Valence × Intensity boost reflects informational value

With Fix 1 in place, the recall priority boost:

```csharp
var priorityBoost = 1 + MaxPriorityBoost × Intensity × |Valence|
```

now means: "surprising entries surface more" rather than "positive-outcome
entries surface more." This is the correct behavior — the most informative
entries (high surprise, either direction) should be recalled preferentially
for validation.

---

## Consequences

### Positive

- **Repetition ≠ confidence** — dedup merge no longer inflates confidence;
  ten identical commits without evidence produce the same confidence as one
- **Contradiction is effective** — a single contradiction weakens Strength,
  increases Variance, and shifts priority toward re-examination
- **Decay works** — the decay sweep actually persists Strength changes,
  ensuring unstable beliefs fade over time
- **Priority is information-driven** — surprising entries surface more,
  regardless of whether the surprise was positive or negative; breaks the
  positive-outcome feedback loop
- **Backward compatible** — all changes only affect the `AffectOptions`-
  active path; systems without `AffectOptions` retain exact current behavior

### Negative

- **Dedup merge is weaker** — previously, duplicate commits gradually
  increased confidence through repetition, which was a simple (if biased)
  signal. Now confidence only changes through the PE path, which requires
  `Reward` signals.
- **ContradictAsync is stronger** — a single contradiction now has
  significant impact on Strength and Variance. For domains where
  contradictions are noisy (e.g., game losses that aren't the pattern's
  fault), this may be too aggressive. Tunable via `LearningRate`.

### Risks

| Risk | Mitigation |
|---|---|
| Entries committed without `Reward` never change confidence | Offline learner curiosity walk provides `Reward` via intrinsic evaluation; covers entries that agents don't explicitly reinforce |
| Too-aggressive contradiction in noisy domains | `LearningRate` is configurable; contradiction applies `2 × LearningRate`, so tuning LR from 0.1 to 0.05 halves the impact |
| Dedup without confidence bump may leave many low-confidence entries | Observation count tracks repetition; consolidation criteria can include it as a promotion signal |

---

## Future work (not in this change)

1. **Exploration slot in recall** — Reserve one slot in top-K results for a
   low-confidence / high-surprise entry, ensuring the curiosity mechanism
   also operates during active use (not just offline). This is the ε-greedy
   mechanism ADR-008 §2 proposed and deferred.

2. **Source diversity tracking** — Track unique evidence sources (not just
   evidence strings). Reinforcement from 3 independent sources should weight
   more than 10 reinforcements from the same source.

3. **Coherence measured against knowledge store** — The offline learner's
   `ComputeCoherence` currently measures coherence against sibling empirical
   entries. This creates echo chambers. Coherence should be measured against
   the knowledge store (external evidence), not the system's own beliefs.

4. **Consolidation reversibility** — Add a path to re-open consolidated
   entries when new contradicting evidence arrives against the promoted
   knowledge document.

---

## References

- ADR-008: Affective Signals for Empirical Memory — original proposal for
  prediction-error reinforcement, decay, and priority boosting
- ADR-008 §2 "Self-reinforcement loops are reduced but not eliminated" —
  identified the recall → reinforce feedback loop this ADR addresses
- Rescorla-Wagner learning rule — prediction-error-driven learning;
  the asymmetric treatment of confirmation vs. disconfirmation follows
  from the principle that surprising evidence carries more information
- Popper, K. (1959) "The Logic of Scientific Discovery" — falsifiability
  as the basis of scientific knowledge; disconfirmation asymmetry is the
  computational analog
