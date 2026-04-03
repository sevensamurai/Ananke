# ADR-002 — LogEventsDemo: Empirical Memory from Simulated Operations Logs

**Status:** Proposed  
**Date:** 2025-07-14  
**Authors:** Team  
**References:** [ADR-001](001-ars-contexta-cognitive-architecture-patterns.md), Guides [15](../guides/15-empirical-memory.md), [15a](../guides/15a-empirical-memory-tuning.md)

---

## Context

Ananke's empirical memory subsystem (`IEmpiricalMemory`, `IOfflineLearner`,
`IPredictionSource`) has been designed and tested against structured game scenarios
(Connect4Demo). The next step is to validate these components against a more
realistic operational domain: **infrastructure and application log analysis**.

### The scenario

A simulated distributed system consisting of:

| Component | Role | Infra |
|---|---|---|
| **UI / API Gateway** | User-facing HTTP endpoints | — |
| **Background Workers** | Async job processing (queue consumers) | Redis (queue + cache) |
| **Reporting Backend** | Aggregation, scheduled reports | PostgreSQL (relational), MongoDB (document store) |
| **IoT / Event Ingestion** | Device telemetry, event routing | MQTT broker |

The system produces structured log events, alerts, and operational telemetry.
**Transient errors** (network blips, GC pauses, connection pool exhaustion) occur
stochastically. **Coding errors** (null reference in a new deploy, schema mismatch
after migration, race condition under load) cascade across the stack with causal
delays.

### What the demo should show

1. **User-driven exploration** — a human reads logs, investigates alerts, forms
   hypotheses. The system observes what the user looks at and what they conclude.
2. **Recall of similar past incidents** — when the user encounters a new error, the
   system recalls empirical entries from structurally similar past events.
3. **Autonomous investigation** — the `OfflineLearner` curiosity walk explores
   entries the user hasn't looked at, finds correlations, and surfaces discoveries.

### Key constraint

> **Minimal to no LLM usage.** The demo must remain inexpensive to run and fast to
> iterate. LLM calls should be optional enrichments, not load-bearing components.

---

## Options Analyzed

### Option A — Full Simulation with In-Process Log Generator

**Architecture:** A single console application generates synthetic log streams from
all services, processes them through `IEmpiricalMemory`, and runs `OfflineLearner`
cycles. No external infrastructure. No LLM.

```
┌────────────────────────────────────────────────────────┐
│                   LogEventsDemo (console)               │
│                                                        │
│  ┌──────────────┐    ┌─────────────────┐               │
│  │ LogSimulator  │───▶│ LogEventStream  │               │
│  │ (all services)│    │ (Channel<T>)    │               │
│  └──────────────┘    └────────┬────────┘               │
│                               │                        │
│         ┌─────────────────────┼──────────────┐         │
│         ▼                     ▼              ▼         │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │
│  │ PatternDetect│  │ Interactive  │  │ OfflineLearner│  │
│  │ (rule-based) │  │ Explorer     │  │ (curiosity)   │  │
│  │              │  │ (user REPL)  │  │               │  │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘  │
│         │                 │                 │          │
│         └─────────────────┼─────────────────┘          │
│                           ▼                            │
│                  ┌─────────────────┐                   │
│                  │InMemoryEmpirical│                   │
│                  │    Memory       │                   │
│                  └─────────────────┘                   │
└────────────────────────────────────────────────────────┘
```

**Components:**

| Component | Implementation | LLM? |
|---|---|---|
| **LogSimulator** | Generates `LogEvent` records with timestamps, service names, severity, structured fields. Uses probability distributions for transient errors and scripted failure cascades for coding errors. | No |
| **PatternDetector** | Rule-based sliding-window detector. Recognizes temporal correlations (e.g., "Redis ETIMEDOUT within 5s of Worker OOM"), co-occurring error codes, error-rate spikes. Produces `EmpiricalEntry` (Kind=Pattern) with `SemanticDescription.FromTags(...)`. | No |
| **Interactive Explorer** | Console REPL. Commands: `tail <service>`, `grep <pattern>`, `investigate <entry-id>`, `correlate <timerange>`, `recall <situation>`. Each user action is logged; recall results are shown with confidence scores. | No |
| **OfflineLearner** | Existing `InMemoryOfflineLearner`. `ISimulationSource` replays log windows; `TagOverlapPredictionSource` forms predictions. Curiosity walk discovers patterns the user hasn't explored. | No |
| **Knowledge base** | Static JSON/YAML files: system architecture (Mermaid diagram parsed to tags), service dependency graph, known failure modes wiki. Loaded as `EmpiricalEntry` (Kind=Heuristic, Source="wiki") at startup. | No |

**Embedding strategy (no LLM):**
- Use `SemanticDescription.SemanticTags` exclusively — no vector embeddings.
- Tags are derived structurally from log fields: `service:api-gateway`,
  `error:ETIMEDOUT`, `cause:connection-pool-exhaustion`, `infra:redis`.
- `TagOverlapPredictionSource` already works on tag overlap, not vector similarity.
- `InMemoryEmpiricalMemory` can be configured with a no-op embedding model (returns
  zero vectors) since recall scoring uses tag overlap when tags are present.

**Pros:**
- Zero external dependencies — `dotnet run` and go.
- Zero LLM cost — all detection is structural/rule-based.
- Exercises all empirical memory APIs: `CommitAsync`, `RecallAsync`,
  `ReinforceAsync`, `ContradictAsync`, `OfflineLearner.LearnAsync`.
- Deterministic and reproducible — scripted failure scenarios.
- Can be extended later with optional LLM enrichment.

**Cons:**
- Pattern detection is limited to pre-coded rules.
- No natural language understanding of log messages.
- Interactive explorer is text-only console REPL.

---

### Option B — Log Files + Lightweight Embeddings (Local Model)

Same as Option A, but replaces tag-only similarity with a local embedding model
(e.g., ONNX-exported `all-MiniLM-L6-v2`, ~80MB) for vector similarity in recall.

**Delta from Option A:**

| Change | Detail |
|---|---|
| **Embedding model** | `Microsoft.ML.OnnxRuntime` + `all-MiniLM-L6-v2` ONNX. Runs locally, no API calls. |
| **SemanticDescription** | Populate both `Summary` (natural language) and `SemanticTags`. Embedding uses `ToEmbeddingText()` which combines both. |
| **Recall** | `InMemoryEmpiricalMemory` uses cosine similarity on embeddings + tag overlap as a composite score. |

**Pros:**
- Richer semantic matching — "connection refused" matches "ETIMEDOUT" even without
  explicit tag rules.
- Still zero LLM API cost — model runs locally.
- Better demonstrates the full recall pipeline as designed.

**Cons:**
- ~80MB model download on first run.
- Adds `Microsoft.ML.OnnxRuntime` dependency to the demo.
- Tokenization/embedding adds latency (~5ms per entry on CPU).
- More complex setup than Option A.

---

### Option C — Simulated Logs + Agent with Tool-Based Investigation

Extends Option A/B with an agent loop. The agent has tools to investigate the
simulated system and uses `IEmpiricalMemory` to learn over multiple investigation
sessions. Requires an LLM.

**Delta from Option A:**

| Change | Detail |
|---|---|
| **Agent** | Ananke agent with system prompt describing the operations domain. |
| **Tools** | `read_logs(service, timerange, severity)`, `read_events(type, timerange)`, `query_metrics(service, metric)`, `read_commits(service, timerange)`, `lookup_architecture(component)`, `recall_empirical(situation)`, `commit_insight(...)`, `reinforce_empirical(...)` |
| **Session model** | Multi-turn conversation where the user describes a symptom, the agent investigates using tools, and empirical entries accumulate across sessions. |
| **OfflineLearner** | Runs between sessions. Reports discoveries at start of next session. |

**Pros:**
- Most realistic demonstration of the full Ananke stack.
- Natural language interaction — user describes problems conversationally.
- Agent learns investigation strategies (Kind=Skill) from successful sessions.

**Cons:**
- Requires LLM API key and incurs cost per session.
- Latency per tool call — less responsive than Option A's REPL.
- Harder to reproduce — LLM responses are non-deterministic.
- Scope is significantly larger.

---

### Option D — Hybrid: Structural Core + Optional LLM Enhancement

**Option A as the foundation, with Option C as an optional layer.** The demo runs
fully without an LLM. When an LLM API key is provided via configuration, additional
capabilities activate:

| Capability | Without LLM | With LLM |
|---|---|---|
| **Pattern detection** | Rule-based (sliding window, co-occurrence) | Rule-based + LLM summarizes detected patterns into natural language |
| **Recall** | Tag overlap only | Tag overlap + embedding similarity |
| **Interactive explorer** | Console REPL commands | Console REPL + natural language queries routed to agent |
| **Commit descriptions** | Auto-generated from tags | LLM-refined descriptions |
| **OfflineLearner discoveries** | Tag-based correlation summaries | LLM-written explanations |

**Architecture:**

```
┌──────────────────────────────────────────────────────────────┐
│                    LogEventsDemo                              │
│                                                              │
│  ┌──────────────┐                                            │
│  │ LogSimulator  │  Produces LogEvent stream                  │
│  │              │  (scripted scenarios + stochastic noise)    │
│  └──────┬───────┘                                            │
│         │                                                    │
│         ▼                                                    │
│  ┌──────────────┐     ┌──────────────────────────────┐       │
│  │ PatternDetect│────▶│ IEmpiricalMemory             │       │
│  │ (rules)      │     │ (InMemory, tag-based recall) │       │
│  └──────────────┘     └──────────┬───────────────────┘       │
│                                  │                           │
│  ┌──────────────┐                │                           │
│  │ Knowledge    │  startup load  │                           │
│  │ (arch/wiki)  │───────────────▶│                           │
│  └──────────────┘                │                           │
│                                  │                           │
│  ┌──────────────┐     ┌──────────┴───────────────────┐       │
│  │ Explorer     │◀───▶│ OfflineLearner               │       │
│  │ (REPL/Agent) │     │ (decay + curiosity + consol.)│       │
│  └──────────────┘     └──────────────────────────────┘       │
│                                                              │
│  ┌────────────────────────────────────────────┐              │
│  │ Optional: LLM layer (when API key present) │              │
│  │ - Natural language queries                  │              │
│  │ - Pattern summarization                     │              │
│  │ - Richer commit descriptions                │              │
│  └────────────────────────────────────────────┘              │
└──────────────────────────────────────────────────────────────┘
```

**Pros:**
- Runs with zero cost by default — `dotnet run` works immediately.
- Progressive enhancement — adding an LLM makes it better but isn't required.
- Exercises the full empirical memory lifecycle without artificial constraints.
- Demonstrates the framework's flexibility: same `IEmpiricalMemory` serves both
  rule-based and agent-based consumers.

**Cons:**
- Two code paths to maintain (with/without LLM).
- More complex than Option A alone.

---

## Decision

**Option D — Hybrid: Structural Core + Optional LLM Enhancement.**

Rationale:
1. The primary goal is demonstrating empirical memory — not LLM capabilities.
   Option A's structural approach exercises all the APIs that matter.
2. The "no LLM" constraint maps naturally to `SemanticTags` + `TagOverlapPredictionSource`,
   which were designed for exactly this kind of structured domain.
3. Optional LLM enhancement validates that the same memory store serves both
   autonomous and agent-assisted workflows — a key Ananke design principle.
4. Keeping external dependencies to zero for the base case makes the demo
   accessible and cheap to run repeatedly during development.

---

## Implementation Plan

### Phase 1 — Log Simulation and Data Model

**Goal:** Generate realistic structured log streams from all simulated services.

| Deliverable | Detail |
|---|---|
| `LogEvent` record | `Timestamp`, `Service`, `Level`, `Message`, `StructuredFields` (dict), `CorrelationId`, `SpanId` |
| `LogSimulator` | Configurable per-service error rates, scripted failure cascades (Redis failover → Worker timeout → API 503 chain), stochastic transient noise |
| `FailureScenario` | Declarative scenario format: trigger condition, affected services, cascade delays, error shapes |
| Scenarios library | 5-8 pre-built scenarios covering: Redis connection pool exhaustion, PostgreSQL slow query cascade, MongoDB schema mismatch after deploy, MQTT broker disconnect, Worker OOM from unbounded queue, API null reference from new deploy, reporting timeout from upstream latency |

### Phase 2 — Knowledge Base and Pattern Detection

**Goal:** Seed the system with architectural knowledge and detect patterns from logs.

| Deliverable | Detail |
|---|---|
| Architecture model | Mermaid diagram of the simulated system, parsed to `EmpiricalEntry` (Kind=Heuristic) with tags like `dependency:api→worker`, `infra:redis`, `failure-mode:connection-pool` |
| Service dependency graph | Adjacency list used by pattern detector to understand cascade paths |
| Wiki entries | Known failure modes, runbook fragments, post-mortem summaries — loaded as Heuristic entries with Source="wiki" |
| `RuleBasedPatternDetector` | Sliding-window detector: temporal co-occurrence within configurable windows, error-rate spike detection, service-correlated error clustering. Outputs `EmpiricalEntry` (Kind=Pattern) |
| Commit/deploy log | Simulated Git commit history per service with timestamps. Tool: `read_commits(service, timerange)` returns recent changes |

### Phase 3 — Interactive Explorer (REPL)

**Goal:** Console interface for human-driven investigation.

| Command | Action | Memory interaction |
|---|---|---|
| `tail <service> [n]` | Show last N log events from a service | Records user attention (`service:X` tag) |
| `grep <pattern> [service]` | Search logs by text/field pattern | Records search intent |
| `timerange <start> <end>` | Set investigation time window | — |
| `correlate` | Find correlated events in current time window | Calls `RecallAsync` with situation tags from current window |
| `investigate <entry-id>` | Deep-dive into an empirical entry — show evidence, related entries | Calls `RecallAsync` with entry's tags |
| `commits <service>` | Show recent deploys/changes for a service | — |
| `arch [component]` | Show architecture diagram / component details | — |
| `recall <situation>` | Free-text recall from empirical memory | `RecallAsync` with `SemanticDescription.FromText(situation)` |
| `confirm <entry-id>` | Mark a recalled pattern as confirmed | `ReinforceAsync` |
| `reject <entry-id>` | Mark a recalled pattern as incorrect | `ContradictAsync` |
| `learn` | Trigger an offline learning cycle | `OfflineLearner.LearnAsync()` |
| `status` | Show empirical memory stats | `BrowseAsync` with summary |

### Phase 4 — OfflineLearner Integration

**Goal:** Background learning discovers patterns the user hasn't explored.

| Deliverable | Detail |
|---|---|
| `LogSimulationSource : ISimulationSource` | Replays a log time window and evaluates whether a pattern's condition/effect pair manifests. Returns reward signal for intrinsic reinforcement. |
| `TagOverlapPredictionSource` | Already implemented — forms predictions from reinforced neighbors. |
| Curiosity reporting | `OfflineLearner` discoveries surfaced to user at next REPL prompt: "While you were away, I found: ..." |
| Decay configuration | Tune `BaseDecayRate`, `VarianceDecayRate`, `DeletionThreshold` for the log domain — entries should persist longer than game moves but still fade if never reinforced. |

### Phase 5 — Optional LLM Layer

**Goal:** Progressive enhancement when API key is available.

| Deliverable | Detail |
|---|---|
| `nl <question>` REPL command | Routes natural language to an Ananke agent with log-reading tools |
| Agent tool kit | `read_logs`, `read_events`, `query_metrics`, `read_commits`, `lookup_architecture` + `EmpiricalMemoryTools.Create(...)` |
| Pattern summarization | After rule-based detection, LLM writes a human-readable description for the `EmpiricalEntry.Description.Summary` |
| Feature flag | `--llm` CLI flag or `LLM__ApiKey` env var. When absent, all LLM features are no-ops. |

---

## Key Design Decisions

### Why tag-based recall works without embeddings

The simulated system has a **closed vocabulary** — service names, error codes,
infrastructure components, and failure modes are all known at design time. This
means:

1. `SemanticTags` can be derived deterministically from `LogEvent.StructuredFields`.
2. `TagOverlapPredictionSource` produces meaningful predictions from structural
   similarity — no semantic ambiguity to resolve.
3. `RecallAsync` with tag-populated `SemanticDescription` matches on causal
   dimensions (cause, effect, service, infra) rather than surface text similarity.

This is exactly the domain where tag-based empirical memory excels: structured,
finite, causally connected.

### Why not a real distributed system

Running actual Redis, PostgreSQL, MongoDB, and MQTT instances would:
- Require Docker or cloud infrastructure.
- Make the demo non-deterministic (real timing, real failures).
- Obscure the empirical memory behavior behind infrastructure noise.

The simulated system produces **the same log shapes** as real infrastructure but
with deterministic timing, scriptable failures, and instant replay — ideal for
demonstrating and testing the learning pipeline.

### Embedding fallback for `InMemoryEmpiricalMemory`

`InMemoryEmpiricalMemory` requires an `IEmbeddingModel`. For the no-LLM path, a
`ZeroEmbeddingModel` that returns zero vectors will be provided. This makes
embedding-based cosine similarity return 0 for all pairs, effectively disabling
it — recall falls through to tag overlap scoring, which is the intended path.

---

## Consequences

- **LogEventsDemo** becomes the reference implementation for empirical memory in
  operational domains.
- The tag-based recall path gets production-quality testing and tuning.
- `ISimulationSource` gets its first non-game implementation.
- The hybrid architecture validates Ananke's design principle that memory layers
  work independently of the intelligence layer (LLM vs. rules).
- Future demos (real log ingestion, OpenTelemetry integration via `Ananke.OpenTelemetry`)
  can build on the same log data model and pattern detector.
