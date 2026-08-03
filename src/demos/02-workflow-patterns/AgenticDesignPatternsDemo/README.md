# AgenticDesignPatternsDemo — 14 Agentic Patterns

A runnable catalogue of **14 recognized agentic design patterns** implemented with Ananke. All patterns use simulated models — no API keys or external services required.

---

## Quick Start

```bash
cd demos/AgenticDesignPatternsDemo
dotnet run
```

All 14 patterns run sequentially and print their output to the console.

---

## What the Demo Shows

| # | Pattern | Key APIs |
|---|---|---|
| 1 | **Single Agent** — ReAct tool-calling loop | `AgentJobFactory`, `ToolKit`, `Workflow<T>` |
| 2 | **Sequential Chain** — linear pipeline of jobs | `.Chain()` |
| 3 | **Parallel Fork / Join** — concurrent branches with merge | `Workflow.Fork()`, `.Join()` |
| 4 | **Router / Coordinator** — conditional dispatch | `Workflow.Decide<T>()` |
| 5 | **Loop Primitive** — job loop with termination condition | manual `Then` back-edge |
| 6 | **Review / Critique** — draft → critique → revise | multi-agent job sequence |
| 7 | **Iterative Refinement** — score-gated quality loop | `Workflow.Decide<T>()` |
| 8 | **Human-in-the-Loop** — interrupt → resume with review | `ICheckpointStore`, checkpoint resume |
| 9 | **Sub-flow Composition** — nested workflows | `SubFlow()` |
| 10 | **Agent Middleware** — cross-cutting concerns | `IAgentMiddleware<T>` |
| 11 | **Context Strategy** — conversation window management | `IAgentContextStrategy` |
| 12 | **Budget Tracking** — token / cost budgets per run | `AgentBudget` |
| 13 | **Streaming Chat** — token-level SSE streaming | `StreamingChatWorkflow` |
| 14 | **Workflow Streaming** — workflow-event stream | `IAsyncEnumerable<WorkflowEvent>` |

---

## Project Structure

| File | Purpose |
|---|---|
| `Program.cs` | Entry point; calls `Demo01_…` through `Demo14_…` in sequence |
| `SimulatedModel.cs` | Deterministic `IStreamingAgentModel` stub for offline use |

---

## Infrastructure

None — all LLM responses are simulated. No `secrets.json` needed.

---

## Related

- Guide: [16 — Agentic Design Patterns](../../../../docs/guides/16-agentic-patterns.md)
- Package: [Ananke.Orchestration](../../../Ananke.Orchestration/README.md)
- Category page: [02 — Workflow Patterns](../../../../docs/demos.md)
