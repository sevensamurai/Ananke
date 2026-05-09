<!-- topic: concepts, tags: concepts, overview, workflow, agent, tools, memory, empirical, state-machine, distributed, mcp -->
# Concepts

A high-level tour of Ananke's core building blocks. Each section links to the in-depth guide for further reading.

---

## Workflows

A **workflow** is a typed, directed graph of jobs. You define a plain C# record as your state type, wire jobs with `.Then()` / `.Fork()` / `.Join()`, and call `.RunAsync()`. The graph engine handles scheduling, parallelism, sub-workflow composition, and real-time event streaming — your job code stays pure and testable.

Key primitives: sequential chains, conditional routing via `Workflow.Decide<T>`, parallel fan-out/fan-in with `Workflow.Fork` + `.Join()`, and nested `SubFlow`.

[Learn more →](guides/02-workflows.md)

---

## Agents

An **agent** is an LLM wrapped behind the `IStreamingAgentModel` interface. An `AgentJob` drops that LLM into any workflow step — you supply a system prompt, user prompt template, optional tools, and a mapping from the model's response to your state type.

Because the provider is injected as an interface, you can swap OpenAI for Anthropic, Google Gemini, or a local Ollama endpoint without changing workflow code. Structured output (JSON schema) works the same across all providers.

[Learn more →](guides/03-agents.md)

---

## Tools

**Tools** are named functions with a description and typed parameters. The LLM reads the description and parameter schema to decide when and how to call them. Tools are grouped into a `ToolKit` and wired into an agent. Both sync and async delegates are supported; generic overloads produce accurate JSON Schema types automatically.

[Learn more →](guides/04-tools.md) · [Full reference →](reference/tools-reference.md)

---

## Memory

Ananke provides two complementary memory layers:

**Long-term memory (RAG):** Ingest documents and retrieve relevant chunks via semantic search. Backed by vector stores (configurable); the default implementation works in-memory for tests, plugs into external stores for production.

**Empirical memory:** Agents accumulate *episodes* (interaction records) and *skills* (validated patterns distilled from episodes). An offline learner scores episodes with Monte Carlo reward propagation and promotes high-value patterns to skills. Skills can be exported as packages and shared across projects.

[Long-term memory →](guides/06-memory.md) · [Empirical memory →](guides/15-empirical-memory.md) · [Memory tuning →](guides/15a-empirical-memory-tuning.md)

---

## State Machine & Distributed

Every workflow runs inside a **state machine** that supports interrupts, a middleware pipeline, circuit breaking, and distributed locking via RedLock. Workflow state is fully serialisable — you can pause execution, persist the checkpoint (to Redis, SQL, or any adapter), and resume from a different process.

The distributed layer adds Redis-backed persistence and horizontal scaling, enabling multi-replica deployments without coordination ceremony.

[State machine →](guides/08-state-machine.md) · [Distributed →](guides/09-distributed.md)

---

## Human-in-the-Loop

Workflows can pause at any point and wait for human input — an approval decision, a form submission, or an arbitrary signal. State is persisted at the pause checkpoint. When the signal arrives (via API, webhook, or polling), execution resumes exactly where it left off.

[Learn more →](guides/07-human-in-the-loop.md)

---

## MCP & Interop

Ananke workflows can be **exposed as MCP (Model Context Protocol) servers**, making them callable by any MCP client (Copilot, Claude, custom agents). The Agent-to-Agent (A2A) protocol layer enables structured, typed communication between independently hosted agents.

[Learn more →](guides/12-mcp-and-interop.md)

---

## Agentic Patterns

`AgenticPattern` is a library of pre-wired workflow builders for recognised orchestration patterns. Patterns are discoverable via IntelliSense, validate at `Build()`, and generate named topologies rather than ad-hoc graphs.

Available patterns: **Review & Critique** (generator → critic loop until approval), **Iterative Refinement** (single-agent refinement loop). More are added each release.

[Learn more →](guides/16-agentic-patterns.md)

---

## Observability

All workflow executions emit structured logs, distributed traces (OpenTelemetry), and metrics. The telemetry is designed to be infrastructure-agnostic — works with Application Insights, Jaeger, Prometheus, or any OTLP collector.

[Learn more →](guides/10-observability.md)

---

## What's Next

- Work through the learning path → [Learning Path](learning-path.md)
- Browse runnable projects → [Demos](demos.md)
- See the CLI workflow loop → [CLI Overview](cli/overview.md)
- Look up APIs → [API Reference](reference/features.md)
