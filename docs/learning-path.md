<!-- topic: learning-path, tags: tutorials, learning-path, guides, index, learning, foundation, workflows, agents, tools, memory, distributed, advanced -->
# Learning Path

This is the self-guided path through Ananke. If you are new to the framework, start at the top and move downward. If you already know the area you care about, jump directly to that section and use the guide links as entry points.

The structure is intentionally progressive:
- Foundation establishes the core workflow and agent model.
- Building Applications turns those primitives into user-facing systems.
- Infrastructure covers persistence, distributed execution, and observability.
- Advanced focuses on interoperability, testing, tuning, and reusable patterns.

If you want runnable projects alongside these guides, use [Demos](demos.md).

---

## Recommended Sequences

| Goal | Suggested order |
|---|---|
| Build a chatbot | 01 → 03 → 04 → 05 |
| Build a stateful chatbot | 01 → 03 → 04 → 05 → 08 |
| Build an agentic workflow | 01 → 02 → 03 → 04 → 07 |
| Add RAG / document Q&A | 01 → 03 → 04 → 06 |
| Add learning from experience | 01 → 03 → 04 → 06 → 15 |
| Prepare for production | 01 → feature path → 10 → 11 → 14 |

---

## Foundation

Start here if you're new to Ananke.

- **[01 — Getting Started](guides/01-getting-started.md)** — Install the packages, build your first workflow, make your first LLM call.
- **[02 — Workflows](guides/02-workflows.md)** — Conditional routing, fork/join parallelism, sub-workflows, and real-time event streaming.
- **[03 — Agents](guides/03-agents.md)** — LLM providers (OpenAI, Anthropic, Google), structured output, multi-provider capability routing.
- **[04 — Tools](guides/04-tools.md)** — Tool calling, function calling, typed parameters, async tools, and ToolKit composition.

---

## Building Applications

Practical patterns for production applications.

- **[05 — Streaming Chat](guides/05-streaming-chat.md)** — Server-sent events, real-time token streaming, async chat.
- **[06 — Long-Term Memory](guides/06-memory.md)** — Vector embeddings, document ingestion, RAG retrieval, and semantic search.
- **[07 — Human-in-the-Loop](guides/07-human-in-the-loop.md)** — Pause, checkpoint, and resume workflows with human approval.

---

## Infrastructure

Scaling, persistence, and production operations.

- **[08 — State Machine](guides/08-state-machine.md)** — Interrupts, middleware pipeline, circuit breaking, RedLock coordination.
- **[09 — Distributed](guides/09-distributed.md)** — Redis-backed persistence, horizontal scaling, multi-replica coordination.
- **[10 — Observability](guides/10-observability.md)** — Structured logging, distributed tracing (OpenTelemetry), metrics.

---

## Advanced

Push Ananke to its limits.

- **[11 — Advanced Agents](guides/11-advanced-agents.md)** — Response caching, resilient retries, decorator composition, local LLM endpoints.
- **[12 — MCP & Interop](guides/12-mcp-and-interop.md)** — Expose workflows as MCP servers, Agent-to-Agent (A2A) protocol.
- **[13 — Design Tooling](guides/13-design-tooling.md)** — Visual workflow designer, graph editor, Mermaid diagram export.
- **[14 — Testing](guides/14-testing.md)** — Unit testing, integration testing, in-memory providers, NUnit test harness.
- **[15 — Empirical Memory](guides/15-empirical-memory.md)** — Episodes, skills, Monte Carlo reward propagation, skill packaging.
- **[15a — Memory Tuning](guides/15a-empirical-memory-tuning.md)** — Decay parameters, importance scoring, offline learning optimisation.
- **[16 — Agentic Patterns](guides/16-agentic-patterns.md)** — Review & Critique, Iterative Refinement, and the full pattern catalog.
- **[UV Setup — Python Interop](guides/uv-setup-for-dotnet-developers.md)** — Run Python, Node.js, and Docker tools from Ananke via the external skill catalog.

---

## Where to go next

- Prefer reading concepts first → [Concepts](concepts.md)
- Want runnable projects → [Demos](demos.md)
- Want CLI command walkthroughs → [CLI Overview](cli/overview.md)
- Need an API lookup → [API Reference](reference/features.md)