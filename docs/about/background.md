<p align="center">
  <img src="ananke-creation.png" alt="Ananke — Stability Before Creation" width="680" />
</p>

<p align="center">
  <em>"Even the gods bowed to Ananke, for she alone could not be moved."</em><br/>
  — Adapted from Aeschylus & Plato
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/Ananke"><img src="https://img.shields.io/nuget/v/Ananke.svg?label=Ananke&color=5B4FCF" alt="NuGet" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="License" /></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10" />
</p>

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

---

## The Parallel

Software systems have the same structure.

An AI agent that can call tools, route between models, and orchestrate multi-step
workflows is powerful — but only if the infrastructure underneath is stable. When the
underlying contracts shift, when state is untyped, when coordination is ad-hoc,
the system becomes fragile at exactly the moment it needs to be reliable.

Ananke the framework starts from the same principle as Ananke the goddess:

> **Fix the rules first. Then let everything else move.**

This isn't a metaphor bolted on after the fact. It's the actual design sequence:

| Mythological concept | Framework principle | Where it shows up |
|---|---|---|
| **Necessity precedes creation** | Infrastructure before features | Typed state, distributed locks, and checkpointing exist before any LLM call is made |
| **Immutable laws** | Contracts are non-negotiable | `IStreamingAgentModel`, `IJob<T>`, `IDistributedLock` — interfaces that don't bend to a specific provider |
| **Ananke cannot be moved** | The core is vendor-agnostic | Swap OpenAI for Anthropic for Google — the workflow doesn't change |
| **Ananke + Chronos encircle creation** | State machine + workflow together | `AbstractStateMachine` (the rules) and `Workflow<T>` (the flow) are the two pillars; Bridge connects them |
| **The cosmos emerges from the egg** | Complex systems compose from simple parts | Fork/join, sub-workflows, agent handoff — all built from the same `IJob<T>` primitive |
| **Gods cannot override necessity** | No escape hatches | State is typed end-to-end. If the compiler doesn't accept it, the workflow won't run it |

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

---

## The Short Version

Ananke is named after the force that said: *the rules come first, and everything
else follows.*

That's also the framework's design philosophy. Fix the contracts. Type the state.
Make the infrastructure swappable. Then let agents, workflows, and state machines
do their work — knowing the ground beneath them won't shift.

---

← [Back to README](../../README.md)
