# LogEventsDemo — Empirical Memory from Operations Logs

A simulated distributed system produces structured log events. **Rule-based pattern detection** finds cascading failures. An **interactive REPL** lets you investigate incidents, recall past events from empirical memory, and confirm or reject detected patterns. **Offline learning** then discovers correlations you haven't explored manually.

No LLM or external services required.

---

## Quick Start

```bash
cd demos/LogEventsDemo
dotnet run                        # 200 ticks + interactive REPL
dotnet run -- --ticks 500         # generate 500 ticks of log data
dotnet run -- --auto              # run simulation, detect, learn, print report (no REPL)
```

---

## What the Demo Shows

### Phase 1 — Seed Knowledge Base

Architectural heuristics are loaded into `InMemoryEmpiricalMemory` as `EmpiricalKind.Heuristic` entries — e.g. "database CPU spike precedes API latency within 30 s".

### Phase 2 — Log Simulation

`LogSimulator` emits `LogEvent` records from a `SystemTopology` (services + dependencies). `FailureScenario` objects inject realistic cascading faults. Events are tagged by `LogTagExtractor`.

### Phase 3 — Pattern Detection

`RuleBasedPatternDetector` scans the event stream for known cascade signatures (timeout chains, CPU spike correlations, restart storms). Detected patterns are printed and stored as `EmpiricalKind.Episode` entries.

### Phase 4 — Interactive REPL (default mode)

`Explorer` provides a console REPL for the memory:

| Command | Description |
|---|---|
| `browse` | List all stored episodes and heuristics |
| `recall <query>` | Semantic search over empirical memory |
| `confirm <id>` | Upvote a detected pattern |
| `reject <id>` | Downvote / suppress a detected pattern |
| `learn` | Trigger `OfflineLearner` to explore correlations |
| `report` | Print a structured learning report |
| `quit` | Exit |

### Phase 5 — Offline Learning

`OfflineLearner` uses `TagOverlapPredictionSource` with UCB curiosity scoring to simulate counterfactual scenarios (via `LogSimulationSource`) and surface correlations with high evidence weight.

---

## Project Structure

| File | Purpose |
|---|---|
| `Program.cs` | Entry point; orchestrates phases 1–5 |
| `LogEvent.cs` | Core event record |
| `SystemTopology.cs` | Service graph with dependency edges |
| `FailureScenario.cs` | Fault injection definitions |
| `LogSimulator.cs` | Tick-based event emitter |
| `LogSimulationSource.cs` | `ISimulationSource` adapter for `OfflineLearner` |
| `LogTagExtractor.cs` | Tags events by pattern type |
| `RuleBasedPatternDetector.cs` | Cascade failure detection rules |
| `KnowledgeBase.cs` | Heuristic seed data |
| `Explorer.cs` | Interactive REPL |
| `SimulatedCommitLog.cs` | Supporting log infrastructure |
| `ServiceDefinition.cs` | Service metadata records |

---

## Infrastructure

None — runs entirely in-process. No API keys, no Docker.

---

## Related

- Guide: [10 — Observability](../../../../docs/guides/10-observability.md)
- Guide: [15 — Learning Primitives](../../../../docs/guides/15-empirical-memory.md)
- Package: [Ananke.Learning](../../../Ananke.Learning/README.md)
- Package: [Ananke.OpenTelemetry](../../../Ananke.OpenTelemetry/README.md)
- Category page: [04 — Organics & Emergence](../../../../docs/demos.md)
