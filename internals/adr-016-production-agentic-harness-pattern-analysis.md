# ADR-016 — Production-Grade Agentic Harness Patterns: Comparative Analysis

| Field          | Value                                                                                                                                 |
|----------------|---------------------------------------------------------------------------------------------------------------------------------------|
| **Status**     | Informational                                                                                                                         |
| **Date**       | 2025-07-29                                                                                                                            |
| **Authors**    | —                                                                                                                                     |
| **Deciders**   | Ananke maintainers                                                                                                                    |
| **Tags**       | memory, multi-agent, consolidation, intent-classification, background-processes, coordinator, empirical-memory, architecture-analysis |
| **Relates to** | ADR-007 (background cognitive processes), ADR-008 (affective signals), ADR-009 (confirmation bias), ADR-010 (structured knowledge identity), ADR-012 (Ars Contexta patterns), ADR-013 (agentic design pattern readiness), ADR-014 (empirical memory skill learning) |

---

## Context

A circulating technical analysis describes the internals of a production-grade
agentic harness, attributed to an unreleased build of a commercial AI coding
assistant. The original source cannot be verified and should be treated as
**speculative engineering analysis** rather than confirmed fact. Several of the
described features (cryptocurrency payment integration, a gamified terminal pet,
an always-on monitoring daemon) read as forward-looking product concepts rather
than shipped mechanisms.

Despite the uncertain provenance, the architectural patterns described are
internally coherent and worth comparing against Ananke's current and planned
design. This ADR performs that comparison as a structured survey. **It proposes
no code changes and does not adopt any pattern described here.** Patterns that
clear the bar of novelty and fit should be tracked in dedicated future ADRs.

One pattern — "Undercover Mode" — is explicitly rejected on ethical grounds and
is not discussed further as a potential adoption target (see §Rejected Patterns).

---

## Patterns Described and Ananke Mapping

| # | Described Pattern | Ananke Status | Gap? |
|---|---|---|---|
| 1 | Skeptical Memory (3-layer index + hint rule) | Partially covered | Partial |
| 2 | Multi-agent swarms with verification phase | Partially covered | Partial |
| 3 | `autoDream` idle consolidation | Covered by design intent | Minimal |
| 4 | `YOLO` permission / implied-intent classifier | Not covered | **Yes** |
| 5 | Undercover Mode | **Rejected** | — |
| 6 | Tiered system prompts (internal vs. external) | Not covered | Minor |
| 7 | KAIROS always-on background daemon | Covered by design intent | Minimal |

---

## Detailed Analysis

### 1 — Skeptical Memory Architecture

**Description:** A three-layer memory system: a lightweight `MEMORY.md` index
(~150-char pointers to where data is stored, not the data itself), topic detail
files pulled in on demand, and a hardcoded "treat your own memory only as a
hint" rule. The agent must re-verify any file's current state via a tool call
before acting on it.

**Ananke coverage:**

Ananke already has three distinct memory layers that map well:

| Described Layer | Ananke Equivalent |
|---|---|
| `MEMORY.md` index (lightweight, always-loaded) | `IKnowledgeStore` with MOC-style index documents (ADR-012, adopted for document organization) |
| Topic detail files (pulled in on demand) | `IKnowledgeStore` semantic recall, `IConversationMemory` window retrieval |
| Learned patterns and heuristics | `IEmpiricalMemory` |

The "treat memory as a hint, verify before acting" principle is **architecturally
equivalent** to the core commitment of ADR-009 (Confirmation Bias Resistance):
high-valence or high-confidence entries do not auto-apply; the agent must
encounter evidence, not just recall. The `IEmpiricalMemory` API enforces this
because agents commit entries separately from acting on them, and
`ContradictAsync` exists as a first-class operation.

**Gap identified:**

The pointer/index discipline — keeping a *navigational* layer that is always
in context and a *detail* layer that is loaded on demand — is not enforced at
the API level for `IConversationMemory`. The conversation window is a flat
sliding window, not a structured index with lazy detail retrieval. ADR-012
adopted this pattern for `IKnowledgeStore` but conversation memory was
explicitly excluded.

**Worth considering:** A structured summary/index of conversation history
(distinct from the raw turn log) that is always present in context, with the
raw turns demoted to on-demand retrieval. This is conceptually adjacent to
ADR-007's background insight surfacing and would reduce context entropy in
long sessions.

---

### 2 — Multi-Agent Swarms with Coordinator, Workers, and Verification Phase

**Description:** A hidden coordinator mode where a single model orchestrates
parallel worker sub-agents (spawned as separate processes). A dedicated
verification phase assigns one worker specifically to adversarially challenge
the output of other workers.

**Ananke coverage:**

| Described Role | Ananke Equivalent |
|---|---|
| Coordinator agent | `DecideWithAgent()` → `AgentRouter<TState>` (ADR-013, well-covered) |
| Parallel workers | `Fork()` / `Join()` with `ForkMode.BestEffort` / `FailFast` |
| Worker-as-separate-process | `HandoffJob` + `IHandoffChannel` (MQTT / InMemory) |
| Cross-agent message passing | A2A protocol (`Ananke.A2A`) |

Ananke supports the structural topology. The coordinator and parallel worker
concepts are fully expressible today.

**Gap identified:**

The **dedicated Verification Phase** — a worker whose sole mandate is to
adversarially attempt to disprove or break the primary worker's output — has no
first-class equivalent in Ananke. ADR-013 §"Gaps" identifies the
**Review & Critique** pattern as missing. This is a concrete, narrow
instantiation of that same gap.

In the described system the verifier is not a general reviewer; it has an
explicit destructive posture ("try to break the code written by another
worker"). This framing is stronger and more useful than a generic critique
step. It maps to the *adversarial probe* sub-type of Review & Critique.

**Worth considering:** When ADR-013's Review & Critique gap is addressed,
the adversarial verifier variant should be included as a named sub-mode, not
only the softer "provide feedback" variant.

---

### 3 — `autoDream` Idle Consolidation

**Description:** A background service that activates on user idle (guarded by
a consolidation lock), scans previous session transcripts, merges fragmented
observations into stable facts, and removes logical contradictions — so the
agent's context is "clean" when the next session begins.

**Ananke coverage:**

This is the most convergent pattern. Ananke's ADR-007 (Background Cognitive
Processes) and ADR-008 / ADR-009 describe essentially the same mechanism:

| `autoDream` concept | Ananke equivalent |
|---|---|
| Activates on user idle | `OnEnter(state, ct => ...)` in a `Thinking`/`Idle` state |
| Consolidation lock | `IDistributedLock` (in `Ananke.Abstractions`) |
| Grep through session transcripts | `IConversationMemory` recall + `IEmpiricalMemory.RecallAsync` |
| Merge fragmented observations → stable facts | `IOfflineLearner.RunOfflineAsync` + `MarkConsolidatedAsync` |
| Delete logical contradictions | `ContradictAsync` + `DeletionThreshold` decay |
| Result: clean, relevant context at next session | `IKnowledgeStore` + `IEmpiricalMemory` pruned and consolidated |

The `autoDream` narrative is a precise description of what `IOfflineLearner`
was designed to do, framed from an operational perspective rather than an API
perspective.

**Gap identified:**

The idle-activation trigger is not wired at the framework level. ADR-007 notes
the gap: there is no mechanism for a background process to surface a
consolidation *result* to the state machine without forcing a state transition.
The infrastructure exists but the idle → consolidate → surface-result lifecycle
is not codified as a reusable framework primitive. This is a known gap already
tracked by ADR-007.

---

### 4 — `YOLO` Implied-Intent Permission Classifier

**Description:** A fast, lightweight ML model (separate from the primary LLM)
that analyses the conversation transcript in real time and decides whether to
auto-approve a tool call based on "implied intent" — executing autonomously
when confidence is high, prompting for confirmation otherwise.

**Ananke coverage:**

Ananke has `InterruptBefore` / `InterruptAfter` checkpoints that pause
execution for human approval, and `ICheckpointStore` to persist and resume
state. This is a **static** interrupt model: the developer declares
checkpoints at workflow definition time; every execution hits those checkpoints
regardless of context.

The described classifier represents a **dynamic, context-sensitive interrupt
policy** — the decision of whether to interrupt is itself a runtime computation
informed by the conversation transcript. This is meaningfully different from
what Ananke provides today.

**Gap identified — most novel concept in this analysis:**

A composable `IInterruptPolicy` interface that can gate tool calls or workflow
transitions based on runtime signals (conversation context, tool risk profile,
prior approvals in the same session) would make Ananke's human-in-the-loop
model significantly more practical for production deployments where blanket
checkpoints generate unacceptable friction.

Key design considerations if this were to be pursued:

- The policy should be pluggable (simple `Func<ToolCallContext, bool>`
  through to a full `IInterruptPolicy` implementation backed by a fast LLM
  or rule engine).
- The policy must not create a new bias vector: auto-approved tool calls in
  a session should not make future calls in the same session *more* likely to
  be auto-approved (i.e., no session-level confidence drift toward unchecked
  autonomy). This is the same structural concern as ADR-009's Recall →
  Reinforce loop, but applied to the interrupt layer.
- Session-scoped "permission grants" (e.g., "user said to proceed without
  asking" after the first checkpoint) are a separate, simpler mechanism and
  likely sufficient for most use cases without a classifier.

This pattern deserves a dedicated ADR if pursued.

---

### 5 — KAIROS Always-On Monitoring Daemon

**Description:** An always-on background daemon that watches the repository
and performs consolidation tasks between sessions.

**Ananke coverage:**

KAIROS is the persistent, cross-session variant of `autoDream`. Architecturally
it combines ADR-007's background cognitive processes with a long-lived process
model. `Ananke.StateMachine`'s `OnEnter` background tasks and the
`IBackgroundWorker` / `BackgroundProcessor` abstractions in
`Ananke.Abstractions` can host this today as a hosted service.

The description is more product-roadmap than architectural novelty. No new
primitives are implied beyond those already tracked in ADR-007.

---

### 6 — Tiered System Prompts (Internal vs. External Users)

**Description:** Different system prompts served to internal engineers
(collaborative, explanatory) versus external users (concise, action-first).
The report suggests brevity was externally enforced for cost/latency reasons
despite being less helpful.

**Ananke coverage:**

Ananke's `IRouter<TState>`, `CapabilityModelRouter`, and `ModelProfile` provide
model-level routing per request. System prompt variation by user type is not
a framework-level concept — it lives in application configuration.

**Minor gap:**

There is no first-class `IPromptPolicy` or prompt variation mechanism in
`AgentJob`. Prompt templates are currently static strings in the job
configuration. A policy slot that allows runtime injection of prompt fragments
based on session context (user tier, verbosity preference, session phase) would
be a small but useful addition. This is already expressible via job
customization but is not a named pattern with a dedicated abstraction.

---

## Summary of Gaps by Priority

| Priority | Gap | Relevant Existing ADR |
|---|---|---|
| **High** | Dynamic interrupt policy (`IInterruptPolicy`) — auto-approve vs. prompt based on runtime intent signals | None — new ADR warranted |
| **Medium** | Adversarial Verifier as a named sub-mode of Review & Critique pattern | ADR-013 (open gap) |
| **Medium** | Structured conversation index (navigational summary always in context, raw turns on demand) | ADR-012 partial |
| **Low** | Idle-consolidation lifecycle primitive in the state machine | ADR-007 (open gap) |
| **Low** | Runtime prompt policy / variation slot on `AgentJob` | ADR-013 context-management gap |

---

## Rejected Patterns

### Undercover Mode

The described feature strips all provider attribution from agent outputs,
suppresses co-authorship metadata in version control, and is designed to make
AI-generated contributions to open-source projects indistinguishable from human
contributions in order to bypass repository policies that prohibit AI-generated
code.

**This pattern is rejected without qualification.** It is not an architectural
pattern — it is a mechanism for deceiving collaborators and circumventing
community governance. Ananke has no obligation to analyze it further for
potential adoption. Any feature request that implements, resembles, or enables
equivalent deception will be closed without further discussion.

---

## Decision

This ADR is **informational only**. No code changes are made or approved here.

The one genuinely novel pattern — the dynamic implied-intent interrupt policy
(§4) — is the only concept in this analysis that does not have a clear existing
home in Ananke's ADR backlog. It merits a standalone ADR if the maintainers
decide to prioritize it.

All other patterns confirm that Ananke's existing design trajectory (ADR-007
through ADR-014) is well-aligned with what production-grade agentic harnesses
converge on. The convergence is reassuring: background consolidation, tiered
memory with disciplined context loading, bias-resistant reinforcement, and
multi-agent coordination with adversarial review are independently identified
as the right primitives by separate teams building production systems.
