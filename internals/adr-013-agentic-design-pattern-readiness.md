# ADR-013 — Agentic Design Pattern Readiness

| Field          | Value                                                                                   |
|----------------|-----------------------------------------------------------------------------------------|
| **Status**     | Proposed                                                                                |
| **Date**       | 2025-07-24                                                                              |
| **Authors**    | —                                                                                       |
| **Deciders**   | Ananke maintainers                                                                      |
| **Tags**       | orchestration, patterns, loop, critique, context, middleware, agent-spawning, budget     |
| **Relates to** | ADR-007 (background cognitive processes), ADR-011 (skill catalog), `Workflow<TState>`, `AgentJob`, `IRouter<TState>`, `IJobMiddleware<T>` |

---

## Context

The agentic AI field is converging on a set of recurring design patterns — documented
by Google Cloud Architecture Center, the Awesome Agentic Patterns catalog (157 patterns,
41 validated in production), and industry practitioners. These patterns form the
vocabulary teams use to reason about agent systems: sequential, parallel, coordinator,
loop, review-and-critique, iterative refinement, hierarchical task decomposition,
swarm, ReAct, and human-in-the-loop.

Ananke already covers a significant subset. This ADR maps the complete pattern
landscape against the current architecture, identifies gaps, and proposes targeted
additions that maximize pattern coverage with minimal architectural disruption.

### Reference Sources

| Source | Scope |
|---|---|
| [Google Cloud — Choose a design pattern for your agentic AI system](https://docs.cloud.google.com/architecture/choose-design-pattern-agentic-ai-system) | 12 patterns organized by workflow type: deterministic, dynamic, iterative, special |
| [Awesome Agentic Patterns](https://www.agentic-patterns.com/patterns) | 157 patterns, 8 categories, 41 field-tested / validated in production |
| Industry consensus (Andrew Ng, LangGraph, CrewAI, ADK docs) | Reflection, tool-use, planning, multi-agent collaboration |

---

## Current Pattern Coverage

### Patterns Well-Supported Today

| Agentic Pattern | Ananke Mechanism | Notes |
|---|---|---|
| **Single Agent** (tools + ReAct loop) | `AgentJob<TState,TResponse>` with `ToolKit` | Tool-call loop in `ExecuteWithToolsAsync` implements Thought→Action→Observation |
| **Sequential** | `.Then("a", "b")` via `DirectConnection` | First-class builder support |
| **Parallel** (fork/join) | `Fork()` / `Join()` with `ForkMode` | `FailFast` and `BestEffort` modes |
| **Coordinator** (LLM-driven dispatch) | `DecideWithAgent()` → `AgentRouter<TState>` | Model reasons over state + tools to pick next job |
| **Human-in-the-Loop** | `InterruptBefore` / `InterruptAfter` + `ICheckpointStore` + `ResumeAsync` | State transform on resume supported |
| **Custom Logic** | `DelegateRouter`, `SubFlow`, `IJobMiddleware<T>`, lifecycle hooks | Arbitrary branching via code |
| **Sub-workflow composition** | `SubFlow<TChild>` with `mapIn` / `mapOut` | Shared checkpoint store and tracer, max-depth guard |
| **Multi-model orchestration** | `CapabilityModelRouter` with `ModelProfile`, `RoutingStrategy` | Per-request cost/speed/intelligence optimization |
| **Resilient model fallback** | `ResilientAgentModel` (429 retry + OTel), `CachingAgentModel` | Composable decorator pattern |
| **Agent-to-agent handoff** | `HandoffJob` + `IHandoffChannel` (MQTT / InMemory) + A2A protocol | Correlated request-response over message broker |
| **Empirical learning** | `IEmpiricalMemory`, `IOfflineLearner`, `ISimulationSource`, `IPredictionSource` | Unique — no other framework offers this |
| **MCP integration** | `Ananke.MCP` — expose tools/workflows as MCP server | Consume external MCP tools via `AddMcpServerToolsAsync` |

### Assessment

Ananke covers ~12 of the 16 major patterns identified across all three reference
sources. The 4 gaps all relate to **iterative/cyclic execution**, **context
management**, **per-LLM-call interception**, and **dynamic runtime decomposition**.

---

## Gap Analysis

### Gap 1 — Loop / Cycle Primitive 🔴 High Impact

**What's missing:** The Google reference identifies **loop**, **review-and-critique**,
and **iterative refinement** as three distinct patterns. All require cycles in the
execution graph with explicit termination conditions. Ananke's workflow builder has
no first-class loop construct.

**Current workaround:** A `DelegateRouter` can route back to a previous job:

```csharp
.Then("critic", Workflow.Decide<S>(s => s.Quality < 0.9 ? "generator" : Workflow.End))
```

**Problems with the workaround:**
- No built-in max-iteration guard → risk of infinite loops and runaway cost
- No automatic loop counter — developers must manually add iteration tracking to `TState`
- The cycle is invisible in the topology — `WorkflowDiagramExtensions` doesn't distinguish
  a loop-back from a forward edge
- No termination reason reporting — did the loop exit because quality was met or because
  iterations were exhausted?

**Patterns unlocked by closing this gap:**

| Pattern | Source |
|---|---|
| Multi-agent loop | Google Cloud |
| Review and critique (generator → critic → revise) | Google Cloud |
| Iterative refinement | Google Cloud |
| Reflection Loop | agentic-patterns.com (established) |
| Self-Critique Evaluator Loop | agentic-patterns.com (established) |
| Spec-As-Test Feedback Loop | agentic-patterns.com (emerging) |

---

### Gap 2 — Context Engineering / Token Budget Management 🟡 Medium-High Impact

**What's missing:** Both agentic-patterns.com ("Context Window Auto-Compaction",
"Curated Code Context Window", "Context-Minimization Pattern", "Semantic Context
Filtering") and Google's guidance on context engineering highlight this as critical
for production agents. Ananke's `AgentJob` passes the full message list to the model
with no window management.

**Specific observations:**
- `AgentJob` has a `_maxContextTokens` field that is **never read** during execution
- No `ITokenizer` or token-counting abstraction exists
- No strategy for compacting old conversation history when approaching model limits
- `StreamingChatWorkflow` loads full history from `IConversationMemory` with no truncation
- No selective context injection — agents get everything or nothing

**Patterns unlocked:**

| Pattern | Source |
|---|---|
| Context Window Auto-Compaction | agentic-patterns.com (validated in production) |
| Context-Minimization Pattern | agentic-patterns.com (emerging) |
| Semantic Context Filtering | agentic-patterns.com (emerging) |
| Progressive Disclosure for Large Files | agentic-patterns.com (emerging) |
| Prompt Caching via Exact Prefix Preservation | agentic-patterns.com (emerging) |

---

### Gap 3 — Agent-Level Middleware (Pre/Post LLM Call Hooks) 🟡 Medium Impact

**What's missing:** `IJobMiddleware<T>` wraps **job execution** but does not intercept
individual **LLM calls** within an `AgentJob`. Each `AgentJob.ExecuteWithToolsAsync`
iteration makes one or more LLM calls that are invisible to the middleware pipeline.

**Consequences:**
- Cannot intercept requests before they reach the LLM provider (PII redaction, prompt
  injection detection, token counting)
- Cannot validate/transform responses after the LLM returns (safety guardrails,
  structured output validation, response logging)
- `ResilientAgentModel` and `CachingAgentModel` prove the decorator pattern works for
  `IAgentModel` — but there's no composable middleware chain, only single-purpose decorators

**Patterns unlocked:**

| Pattern | Source |
|---|---|
| PII Tokenization | agentic-patterns.com (validated in production, established) |
| Lethal Trifecta Threat Model (prompt injection defense) | agentic-patterns.com (best practice) |
| Hook-Based Safety Guard Rails | agentic-patterns.com (validated in production) |
| Structured Output Specification (validation) | agentic-patterns.com (established) |
| CriticGPT-Style Code Review | agentic-patterns.com (validated in production) |

---

### Gap 4 — Dynamic Agent Spawning / Hierarchical Task Decomposition 🟡 Medium Impact

**What's missing:** The workflow topology is fixed at build time. A coordinator agent
can route to pre-defined jobs via `AgentRouter`, but cannot dynamically decompose a
task into N sub-tasks and spawn parallel agent executions at runtime.

**Current situation:**
- `SubFlow` supports static nesting (topology fixed at build)
- `HandoffJob` supports delegation to external agents (but requires pre-configured topics)
- `Fork` targets are declared at build time — cannot be computed at runtime
- No mechanism for "the agent decides it needs 3 parallel research tasks" at runtime

**Patterns unlocked:**

| Pattern | Source |
|---|---|
| Hierarchical task decomposition | Google Cloud |
| Factory over Assistant | agentic-patterns.com (validated in production) |
| Sub-Agent Spawning | agentic-patterns.com (validated in production) |
| Swarm Migration Pattern | agentic-patterns.com (validated in production) |
| LLM Map-Reduce Pattern | agentic-patterns.com (emerging) |

---

### Gap 5 — Execution Budget / Cost Tracking 🟡 Medium Impact

**What's missing:** `CapabilityModelRouter` optimizes per-request cost selection, but
there is no cumulative cost tracking or budget enforcement across a workflow execution.

**Specific observations:**
- `AgentResponse` does not carry token usage metadata (input/output tokens)
- `WorkflowExecution` does not track cumulative cost
- No budget-exceeded termination condition
- No cost observability in the `WorkflowEvent` stream
- The `ModelProfile.CostPer1KTokens` field exists but is only used for per-request
  routing, never for accumulation

**Patterns unlocked:**

| Pattern | Source |
|---|---|
| Budget-Aware Model Routing with Hard Cost Caps | agentic-patterns.com (established) |
| Cost-capped execution | Google Cloud guidance (cost as key pattern selection factor) |
| No-Token-Limit Magic (perf/cost optimization) | agentic-patterns.com (experimental) |

---

### Lower Priority Gaps (P2)

| Gap | Impact | Rationale for deferral |
|---|---|---|
| **Swarm / debate pattern** | Advanced multi-agent | Can be composed from Fork + Handoff + Loop today; demand not yet proven |
| **Tool-level guards** (`IToolGuard`) | Safety | Middleware pipeline partially covers this; add when security audits demand it |
| **Skill catalog ↔ runtime integration** | Dynamic tool discovery | ADR-011 covers the catalog; wiring into agent loop is additive and non-breaking |

---

## Decision

We will address the five high/medium-impact gaps in a phased implementation,
preceded by a cross-cutting **pattern surface layer** that gives all agentic
patterns a single, discoverable home in the API:

| Phase | Gap | Deliverable |
|---|---|---|
| **Phase 0** | Discoverability | `AgenticPattern` static class with per-pattern fluent builders that produce `Workflow<TState>` — the IntelliSense "catalog moment" |
| **Phase 1** | Loop primitive | `Loop()` builder + `LoopConnection` (co-ships with Phase 0) |
| **Phase 2** | Agent middleware | `IAgentModelMiddleware` interface + pipeline decorator |
| **Phase 3** | Context strategy | `IContextStrategy` interface + sliding-window + summarizing implementations |
| **Phase 4** | Cost tracking | `TokenUsage` on `AgentResponse` + budget enforcement |
| **Phase 5** | Dynamic spawning | `MapReduceJob` for runtime-determined parallel agent execution |

### Pattern surface layer rationale

If pattern support is scattered across `Workflow<TState>` builder methods —
`Loop()`, `MapReduce()` alongside `Job()`, `Then()`, `Fork()`, `Join()` —
the agentic features become invisible. A developer searching for "how do I
implement the review-and-critique pattern" won't discover `Workflow<T>.Loop()`.

The `AgenticPattern` class is a dedicated entry point (following the
`StreamingChatWorkflow`, `Handoff`, and `Workflow` static class precedent)
where typing `AgenticPattern.` shows the complete pattern catalog in
IntelliSense. Each method returns a fluent builder that produces a standard
`Workflow<TState>` — composable with checkpointing, tracing, sub-flows,
and all existing infrastructure.

This serves the power user persona: developers who already understand agentic
design patterns and want a direct path from pattern name to working code.
They are the most likely to push boundaries and provide feedback.

The underlying primitives (`Loop()`, `MapReduce()` on `Workflow<TState>`)
remain available for developers who need custom pattern variants. The pattern
layer is sugar for discoverability and correctness-by-construction, not a
restriction on composition.

Each phase is independently shippable, backward-compatible, and testable with
in-memory implementations.

---

## Consequences

### Positive

- Ananke will support all 12 patterns identified by Google Cloud Architecture Center
- The `AgenticPattern` class gives power users a curated, IntelliSense-discoverable
  catalog of recognized patterns — typing `AgenticPattern.` reveals the full menu
- Developers can implement reflection, iterative refinement, and generator-critic
  patterns without manual loop wiring
- Pattern builders validate required parts at `Build()` time with clear error messages,
  preventing misconfigured patterns from reaching runtime
- Context management prevents production agents from silently failing when conversation
  history exceeds model limits
- Agent-level middleware enables security patterns (PII, prompt injection) without
  modifying provider implementations
- Cost tracking gives operators visibility and control over LLM spend
- Documentation has a single landing page (`docs/guides/16-agentic-patterns.md`) that
  maps industry pattern names to Ananke methods — the first thing a power user finds

### Negative

- Increased surface area — 5 new interfaces/abstractions + 1 pattern entry point class
- Loop primitives add complexity to `WorkflowDefinition.Validate()` (cycle detection
  must distinguish intentional loops from topology errors)
- Agent middleware adds a per-call overhead (mitigated: only active when middleware
  is registered)
- Two paths to the same outcome (`AgenticPattern.ReviewCritique()` vs. manual
  `Loop()` wiring) — documentation must be clear about when to use which

### Neutral

- No breaking changes to existing APIs — all additions are opt-in
- Existing demos and tests continue to work unmodified
- The in-memory-first testing contract is maintained for all new abstractions

---

## Alternatives Considered

### Alternative 1: Document patterns as composition recipes, don't add primitives

**Why rejected:** The loop workaround (router-based cycle) is error-prone and
invisible in diagrams. Context management is safety-critical — documenting it as
"bring your own" puts the burden on every consumer. The framework's value proposition
is removing infrastructure burden.

### Alternative 2: Add all patterns at once in a single release

**Why rejected:** Violates the project's incremental release philosophy. Each phase
has different complexity and risk profiles. Phased delivery allows community feedback
between phases.

### Alternative 3: Use the state machine for iterative patterns instead of workflow loops

**Why rejected:** `AbstractStateMachine` supports cycles natively but requires
distributed lock, enum-based states, and a different mental model. For in-workflow
iteration (generate → critique → refine), the workflow builder is the right
abstraction level. The state machine remains the right choice for long-running,
multi-session stateful processes.

### Alternative 4: Add pattern methods directly to `Workflow<TState>` (no separate class)

**Considered:** Add `Workflow<TState>.ReviewCritique(...)`, `.IterativeRefinement(...)`
etc. directly as builder methods alongside `Job()`, `Then()`, `Fork()`.

**Why rejected:** Mixes infrastructure primitives (`Job`, `Then`, `Fork`) with
high-level pattern recipes in a single IntelliSense list. A developer browsing
`Workflow<T>.` would see 25+ methods with no visual grouping. The pattern methods
would be invisible to someone who doesn't already know they exist. A dedicated
`AgenticPattern` class creates a "catalog moment" — the developer types the name,
sees all patterns at once, and each method's XML doc explains the industry pattern
it implements.

### Alternative 5: Separate `Ananke.Orchestration.Patterns` NuGet package

**Considered:** Ship pattern builders in a separate package so the core stays lean.

**Why rejected:** Adds friction — another package to discover and install. The
builders are thin sugar over existing primitives (no new runtime dependencies).
The discoverability goal is better served by being in the same namespace
(`Ananke.Orchestration`) so `AgenticPattern` appears without a new `using`
statement. If the pattern library grows significantly, extraction remains an
option later.

---

## References

- [Google Cloud — Choose a design pattern for your agentic AI system](https://docs.cloud.google.com/architecture/choose-design-pattern-agentic-ai-system)
- [Awesome Agentic Patterns — Full catalog](https://www.agentic-patterns.com/patterns)
- [Ananke Framework Comparison](../docs/about/framework-comparison.md)
- ADR-007 — Background Cognitive Processes
- ADR-011 — Skill Catalog
