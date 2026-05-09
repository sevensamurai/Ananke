<!-- topic: background, tags: about, history, mythology, ananke -->
## Why Ananke?

The AI agent ecosystem is predominantly Python.
For .NET teams shipping to production, that means either adopting a foreign ecosystem
or assembling production infrastructure from scratch.
Even within mature frameworks, capabilities that .NET developers take for granted —
strong typing, real parallelism, dependency injection, structured concurrency —
often require additional libraries, schema definitions, or workarounds.

The landscape is also moving fast.
Frameworks rebrand, merge, or shift direction between releases,
making it risky to couple production systems to a single vendor's roadmap.

Ananke starts from a different question:

> *What does the infrastructure need to look like so that building any agentic system — at any scale — is straightforward for .NET developers?*

The answer is a typed, testable, composable foundation where the infrastructure comes first and LLM providers are pluggable.

---

## Background

In Greek cosmology, **Ananke** (Ἀνάγκη) is the primordial goddess of necessity,
inevitability, and compulsion. She is not one of the Olympians — she precedes them.
In Orphic tradition, Ananke and Chronos (time) together encircled the primordial egg
of creation. When the egg split, the ordered cosmos emerged.

The critical detail: **Ananke came first.** Before time could flow, before matter
could form, the laws had to be fixed. The universe didn't begin with action — it
began with constraint.

Not even Zeus could override necessity. In every Greek source that mentions her,
Ananke is the one force that is non-negotiable. She doesn't act — she defines the
boundaries within which everything else acts.

But Ananke is not the whole story. In Orphic cosmology, once the ordered cosmos burst
from the split egg, the bright upper air — **Aether** (Αἰθήρ) — filled the space
between the heavens. Where Ananke is the constraint that makes existence possible,
Aether is the medium through which existence *reveals itself*. Celestial bodies move
through Aether. Patterns become visible through it. Correspondences — cause and
effect, recurrence, structure — take shape in Aether's luminous space.

In Aristotelian philosophy, Aether is the **fifth element**: incorruptible yet
ceaselessly in motion, the substance from which the stars and their eternal cycles
are made. It appears only after the cosmos exists. It has nothing to do with the
laws — but it depends on them entirely.

---

## The Parallel

Software systems have the same structure.

An AI agent that can call tools, route between models, and orchestrate multi-step
workflows is powerful — but only if the infrastructure underneath is stable. When the
underlying contracts shift, when state is untyped, when coordination is ad-hoc,
the system becomes fragile at exactly the moment it needs to be reliable.

Ananke the framework starts from the same principle as Ananke the goddess:

> **Fix the rules first. Then let everything else move.**

This isn't a metaphor bolted on after the fact. It's the actual design sequence.

### Ananke — the foundation

| Mythological concept | Framework principle | Where it shows up |
|---|---|---|
| **Necessity precedes creation** | Infrastructure before features | Typed state, distributed locks, and checkpointing exist before any LLM call is made |
| **Immutable laws** | Contracts are non-negotiable | `IStreamingAgentModel`, `IJob<T>`, `IDistributedLock` — interfaces that don't bend to a specific provider |
| **Ananke cannot be moved** | The core is vendor-agnostic | Swap OpenAI for Anthropic for Google — the workflow doesn't change |
| **Ananke + Chronos encircle creation** | State machine + workflow together | `AbstractStateMachine` (the rules) and `Workflow<T>` (the flow) are the two pillars; Bridge connects them |
| **The cosmos emerges from the egg** | Complex systems compose from simple parts | Fork/join, sub-workflows, agent handoff — all built from the same `IJob<T>` primitive |
| **Gods cannot override necessity** | No escape hatches | State is typed end-to-end. If the compiler doesn't accept it, the workflow won't run it |

### Aether — the learning layer

The stable foundation is the precondition, not the destination. Once the rules are
fixed, a second principle takes over: connections form, patterns emerge, and the
system begins to learn. That is Aether's role — and the framework has the same
second act.

| Mythological concept | Framework principle | Where it shows up |
|---|---|---|
| **Aether fills the cosmos after creation** | Learning emerges from experience | `IEmpiricalMemory` accumulates patterns, skills, and heuristics from every agent interaction, building on top of the typed foundation |
| **Aether as connective medium** | Connections form across observations | `SemanticDescription` decomposes each entry into weighted causal tags; `IPredictionSource` links entries through prediction-error signals, not just embedding similarity |
| **Aether makes patterns visible** | Hidden structure surfaces over time | `IOfflineLearner` runs background cycles — decay, curiosity walks, consolidation — discovering correspondences that no single conversation could reveal |
| **The fifth element is incorruptible yet in motion** | Confidence derives from variance, not assertion | Each pattern's stability is earned: contradiction reduces it, repeated confirmation raises it — circular self-reporting is not allowed |
| **Imagination before action** | Hypotheses tested without real-world cost | `ISimulationSource` runs self-play, Monte Carlo rollouts, or scenario replays to validate patterns before committing |
| **Mature patterns crystallize into eternal law** | Empirical knowledge becomes canonical | `IConsolidationSummarizer` promotes confident, high-strength entries into `IKnowledgeStore` — closing the loop back to Ananke's domain |

---

## What This Means in Practice

Most agent frameworks start with the LLM and build infrastructure around it.
Ananke inverts that: the infrastructure is the product, and the LLM is a pluggable
component.

This has concrete consequences:

**You can test without an LLM.** Every infrastructure contract (`IDistributedLock`,
`IConversationMemory`, `IKnowledgeStore`, `ICheckpointStore`) has an in-memory
implementation. Integration tests run in milliseconds with no API keys.

**You can swap providers without touching business logic.** The workflow graph,
state types, tool definitions, and routing rules are all provider-independent.
Switching from `gpt-4.1` to `claude-sonnet-4-20250514` is a one-line configuration change.

**You can run distributed without rewriting.** The same `Workflow<T>` that runs
in-memory with `InMemoryHandoffChannel` runs across processes with `MqttHandoffChannel`.
The same `AbstractStateMachine` that uses `InMemoryDistributedLock` in tests uses
`RedisDistributedLock` in production. The topology doesn't change — only the wiring.

**You can compose without limits.** Sub-workflows nest inside parent workflows.
State machines wire into workflow jobs via the Bridge layer. Agent tools trigger
document ingestion pipelines. Everything speaks the same typed contracts, so
composition is mechanical, not architectural.

**Agents compound intelligence over time.** `IEmpiricalMemory` records three kinds
of empirical knowledge — patterns (*"when X, Y follows"*), skills (*"how to investigate X"*),
and heuristics (*"prefer X over Y in situation Z"*). `IOfflineLearner` runs background
cycles between active sessions: decaying stale beliefs, wandering through low-confidence
entries to validate or contradict them, discovering connections across the full memory
corpus. With `ISimulationSource`, the system can rehearse scenarios in imagination —
self-play, Monte Carlo rollouts, or replay — before committing. When a pattern becomes
stable enough, `IConsolidationSummarizer` promotes it into `IKnowledgeStore`, where it
becomes part of the permanent knowledge available to every future agent. Raw LLM
capability is the starting point; every deployment gets smarter.

---

## The Short Version

Ananke is named after the force that said: *the rules come first, and everything
else follows.*

That's also the framework's design philosophy. Fix the contracts. Type the state.
Make the infrastructure swappable. Then let agents, workflows, and state machines
do their work — knowing the ground beneath them won't shift.

Aether is named after what comes next: the luminous medium that fills the ordered
cosmos and makes patterns visible across it. In the framework, that is the empirical
memory and offline learning layer — the part that watches, connects, and remembers,
so the system does not start from zero every time.

Ananke sets the rules. Aether learns them.

---

## License & Commercial Use

Ananke is **free and open source under the [MIT License](https://opensource.org/licenses/MIT)** — for personal, commercial, and enterprise use alike. There are no tiers, no usage fees, and no open-core split. The full framework is free of charge and always will be.

See the [Roadmap & License](roadmap.md) page for the full statement and version plan.

