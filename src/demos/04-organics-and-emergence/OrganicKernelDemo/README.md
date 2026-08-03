# OrganicKernelDemo — Workflow Growth, Division & Learning

Demonstrates the full **organic lifecycle** of an Ananke workflow: a single generalist bookstore agent accumulates tools, detects structural tension, proposes a division, gets approval through a pluggable gate, splits into two specialists, and feeds the outcome back into empirical memory.

No API keys required — all LLM responses are simulated.

---

## Quick Start

```bash
cd demos/OrganicKernelDemo
dotnet run                       # automatic mode
dotnet run -- --supervised       # pause for human approval
dotnet run -- --verbose          # show YAML snapshots & details
dotnet run -- --simulate         # dry-run: propose division but don't spawn/kill
dotnet run -- --no-topology      # skip the colony topology report step
dotnet run -- --supervised -v    # combine flags
```

---

## What the Demo Shows

### Organic Growth Lifecycle

1. **Tool accumulation** — the generalist workflow is loaded with both catalog and order tools.
2. **Complexity sensing** — `OrganicHost` monitors structural tension (tool count, routing entropy) via a `IComplexityMonitor`.
3. **Division proposal** — an experience-driven policy (`IDivisionPolicy`) proposes splitting into `bookstore-catalog` and `bookstore-orders`.
4. **Approval gate** — the proposal passes through `IDivisionApprovalGate`; in `--supervised` mode you approve or reject it interactively.
5. **WorkflowDivider** — spawns the two child workflows, confirms their health, then kills the parent.
6. **Colony topology report** — `ColonyGraphBuilder` builds a graph of cells, domains, and tools from the live capability map and lineage store; `GodNodeDetector` identifies structural single-points-of-failure; `ColonyReportExporter` writes `colony.json` and `COLONY_REPORT.md` under `./out/organic-colony/`.
7. **Empirical memory feedback** — the division outcome is recorded in `InMemoryEmpiricalMemory` so future policies can learn from it.

### Key Abstractions

| Concept | Type |
|---|---|
| Host wiring | `OrganicHost` + `.JoinHost()` |
| Complexity detection | `IComplexityMonitor` |
| Division decision | `IDivisionPolicy` |
| Approval | `IDivisionApprovalGate` (automatic or interactive) |
| Spawning | `WorkflowDivider`, `TypedWorkflowActivatorFactory<T>` |
| Routing after split | `KeywordRequestRouter`, `InMemoryCapabilityMap` |
| Colony graph | `ColonyGraphBuilder`, `GodNodeDetector`, `ColonyReportExporter` |
| Memory | `InMemoryEmpiricalMemory` |

---

## Project Structure

| File | Purpose |
|---|---|
| `Program.cs` | Entry point; orchestrates the full lifecycle |
| `BookstoreTools.cs` | Catalog and order `ToolKit` factories |
| `BookstoreState.cs` | Workflow state record |
| `DemoOptions.cs` | CLI flag parsing (`--supervised`, `--verbose`, `--simulate`, `--no-topology`) |
| `DemoConsole.cs` | Coloured console helpers |
| `Topology/ColonyReportStep.cs` | Post-division topology graph + god-node report |
| `Infrastructure/FakeModels.cs` | Simulated `IStreamingAgentModel` for offline use |

---

## Infrastructure

None — the demo runs entirely in-process with simulated models.

---

## Related

- Package: [Ananke.Organics](../../../Ananke.Organics/README.md)
- Package: [Ananke.Learning](../../../Ananke.Learning/README.md)
- Category page: [04 — Organics & Emergence](../../../../docs/demos.md)
