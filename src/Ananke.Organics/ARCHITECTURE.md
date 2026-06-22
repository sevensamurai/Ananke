# Ananke.Organics — Architecture

> Organic mesh architecture — self-organizing workflow ecosystems with cell division,
> heartbeat sensing, triage routing, domain-affine memory, and evolutionary division policies.

## Role

`Ananke.Organics` is the **growth brain** for a mesh of `Workflow<TState>` instances
(cells). It sits above `Ananke.Learning` and `Ananke.Design` and provides the runtime
machinery for cells to be monitored, divided, healed, routed, snapshotted, and traced
through their lineage.

**What it is not:**
- Not an alternative runner for `Workflow<TState>` — the inner workflow's runner,
  checkpointing, and tracing are never touched.
- Not a distributed orchestrator — it composes with hosting adapters (`InProcessWorkflowHost`
  for dev/tests; external adapters for Docker, Kubernetes, etc.) rather than replacing them.
- Not responsible for request routing after division — `IRequestRouter` / `OrganicHost`
  do post-division dispatch, but load-balancing lifecycle is a future concern (see
  `WorkflowDivider` design notes).

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `OrganicHost` — the growth brain that monitors complexity, evaluates division policies,
   and flows proposals through the approval gate — `src/Ananke.Organics/Kernel/OrganicHost.cs`
2. `IWorkflowHost` — cell lifecycle (spawn / kill / list); default `InProcessWorkflowHost` —
   `src/Ananke.Organics/Kernel/IWorkflowHost.cs`
3. `OrganicWorkflow<TState>` — the execution wrapper over `Workflow<TState>` that feeds
   results into `OrganicHost.ObserveExecution` — `src/Ananke.Organics/Kernel/OrganicWorkflow.cs`
4. `IDivisionPolicy` — proposes a `DivisionPlan` when structural complexity exceeds
   thresholds — `src/Ananke.Organics/Division/IDivisionPolicy.cs`
5. `IWorkflowDivider` — executes an approved division plan: derive → seed → activate →
   spawn → confirm → kill — `src/Ananke.Organics/Division/IWorkflowDivider.cs`

---

## Dependencies

| Dependency | Why |
|---|---|
| `Ananke.Learning` | Division policies recall proven strategies from `IEmpiricalMemory`; `ExperienceDrivenDivisionPolicy` uses `IExplorationStrategy` (UCB) to balance exploitation vs. exploration of split strategies. `DomainAffinityMemory` scopes memory recall to a cell's domain tags. |
| `Ananke.Design` | `WorkflowManifest` is the structural blueprint consumed by `IDivisionPolicy` and `IWorkflowDivider`. `PromptWorkflowDesigner` uses the manifest DSL to grow child workflows from an LLM-generated spec. |

---

## Vertical Slice Map

```
Ananke.Organics/
  Kernel/               Core host and lifecycle
    Lineage/            Birth/death genealogy store
    Snapshots/          Point-in-time mesh capture and restoration
  Division/             Surface-tension evaluation, plan proposal, and execution
    Approval/           Governance gate between proposal and execution
    Review/             Work-product review gates and quorum rules
  Healing/              Error-rate evaluation and recovery planning
  Sensing/              Capability heartbeat aggregation and request routing
```

---

## Namespace → Folder Map

| Namespace | Key Types |
|---|---|
| `Ananke.Organics.Kernel` | `OrganicHost`, `OrganicWorkflow<TState>`, `OrganicWorkflowExtensions`, `IWorkflowHost`, `InProcessWorkflowHost`, `IWorkflowReplicator`, `WorkflowReplicator`, `OrganicGrowthOptions`, `OrganicGrowthOptionsBuilder`, `WorkflowLifecycleEvent` |
| `Ananke.Organics.Kernel.Lineage` | `ILineageStore`, `InMemoryLineageStore`, `CellLineage` |
| `Ananke.Organics.Kernel.Snapshots` | `HostSnapshot`, `WorkflowSnapshotBuilder`, `HostSnapshotExporter`, `IWorkflowActivatorFactory`, `TypedWorkflowActivatorFactory`, `WorkflowActivator`, `PromptWorkflowDesigner` |
| `Ananke.Organics.Division` | `IDivisionPolicy`, `ThresholdDivisionPolicy`, `ExperienceDrivenDivisionPolicy`, `IDivisionTransition`, `StopTheWorldDivisionTransition`, `IWorkflowDivider`, `WorkflowDivider`, `IDivisionOutcomeTracker`, `DivisionOutcomeTracker`, `WorkflowExecutionMonitor`, `ComplexitySnapshot`, `DivisionPlan`, `DivisionResult`, `DivisionSignal`, `DivisionExperience`, `MetabolicSignal`, `MetabolicThresholds`, `StructuralProfile`, `StructuralProfileFactory`, `MemoryProfile`, `FailurePattern`, `FailureClassifier`, `FailureClassifierBuilder`, `FailureClassifierProfiles`, `ToolKitClusterStrategy`, `DomainAffinityMemory`, `IRemoteCellSource`, `DivisionOptions` |
| `Ananke.Organics.Division.Approval` | `IDivisionApprovalGate`, `AutoApprovalGate`, `LlmApprovalGate`, `CallbackApprovalGate`, `MetabolicDivisionApprovalGate`, `BudgetApprovalGate`, `DivisionApproval`. `BudgetApprovalGate` consumes `IBudgetMeter` / `InMemoryBudgetMeter` / `BudgetSpend`, which live in `Ananke.Abstractions.Budget` (`src/Ananke.Abstractions/Budget/`), not this namespace. |
| `Ananke.Organics.Division.Review` | `IWorkReviewGate`, `AutoWorkReviewGate`, `LlmWorkReviewGate`, `CallbackWorkReviewGate`, `QuorumWorkReviewGate`, `WorkReviewQuorum`, `WorkItem`, `WorkItemKind`, `WorkReviewDecision`, `WorkReviewOutcome`, `IWorkReviewParkingStore`, `InMemoryWorkReviewParkingStore`, `ParkingCallbackWorkReviewGate` |
| `Ananke.Organics.Healing` | `IHealingPolicy`, `ThresholdHealingPolicy`, `IHealthMonitor`, `CompositeHealingPolicy`, `HealingPlan`, `HealthSnapshot`, `FailureOrigin`, `AgedCellPrunePolicy`, `IdleCellPrunePolicy` |
| `Ananke.Organics.Sensing` | `ICapabilityMap`, `InMemoryCapabilityMap`, `IMeshAggregator`, `InMemoryMeshAggregator`, `IRequestRouter`, `KeywordRequestRouter`, `IDomainRouter`, `RoutingAffinityTracker`, `QuorumApprovalGate`, `MeshSignal`, `WorkflowSignal`, `SensedCapability` |
| `Ananke.Organics.Topology` | `ColonyGraphBuilder` — builds a `IKnowledgeGraph` colony graph from the live mesh state (capability map, lineage, routing affinity) |
| `Ananke.Organics.Topology.Centrality` | `GodNodeDetector` — identifies over-centralised cells in the colony graph using degree/PageRank centrality |
| `Ananke.Organics.Topology.Reporting` | `ColonyReportExporter` — serialises colony graph snapshots to portable report formats |

---

## Key Abstractions

| Type | Kind | Purpose | Source |
|---|---|---|---|
| `OrganicHost` | `sealed class` | Growth brain — monitors complexity, evaluates division policies, flows proposals through the approval gate, signals division. Does **not** manage cell lifecycle directly. | `src/Ananke.Organics/Kernel/OrganicHost.cs` |
| `IWorkflowHost` | `interface` | Cell lifecycle (spawn / kill / list). Default: `InProcessWorkflowHost`. Production implementations are hosting-adapter concerns. | `src/Ananke.Organics/Kernel/IWorkflowHost.cs` |
| `OrganicWorkflow<TState>` | `sealed class` | Thin execution wrapper over `Workflow<TState>` — feeds results into `OrganicHost.ObserveExecution` after each run. The inner workflow is never modified. | `src/Ananke.Organics/Kernel/OrganicWorkflow.cs` |
| `OrganicWorkflowExtensions.JoinHost` | Static extension | Entry point — `workflow.JoinHost(host, toolKit)` returns the observed wrapper. | `src/Ananke.Organics/Kernel/OrganicWorkflowExtensions.cs` |
| `IDivisionPolicy` | `interface` | Proposes a `DivisionPlan` when surface tension (structural complexity) exceeds thresholds. Returns `null` when healthy. | `src/Ananke.Organics/Division/IDivisionPolicy.cs` |
| `IDivisionApprovalGate` | `interface` | Governance gate between proposal and execution. Default: `AutoApprovalGate`. | `src/Ananke.Organics/Division/Approval/IDivisionApprovalGate.cs` |
| `IBudgetMeter` | `interface` | Reads rolling-window token and cost spend for a workflow key. Default: `InMemoryBudgetMeter`. | `src/Ananke.Abstractions/Budget/IBudgetMeter.cs` |
| `IWorkReviewGate` | `interface` | Reviews work products such as PRs or design docs and returns approved, rejected, revised, or pending decisions. | `src/Ananke.Organics/Division/Review/IWorkReviewGate.cs` |
| `IWorkReviewParkingStore` | `interface` | Persists pending review state across threads (and optionally process restarts). Default: `InMemoryWorkReviewParkingStore`. | `src/Ananke.Organics/Division/Review/IWorkReviewParkingStore.cs` |
| `IWorkflowDivider` | `interface` | Executes the approved plan: derive → seed → activate → spawn → confirm → kill. Atomic — no partial divisions. | `src/Ananke.Organics/Division/IWorkflowDivider.cs` |
| `IHealingPolicy` | `interface` | Evaluates `HealthSnapshot` + `ComplexitySnapshot` to produce a `HealingPlan`. Returns `null` when healthy. | `src/Ananke.Organics/Healing/IHealingPolicy.cs` |
| `ICapabilityMap` | `interface` | Live registry of which domains each cell can handle (populated by heartbeat / sensing signals). | `src/Ananke.Organics/Sensing/ICapabilityMap.cs` |
| `IRequestRouter` | `interface` | Routes a user message to the correct cell by sensing the capability landscape. | `src/Ananke.Organics/Sensing/IRequestRouter.cs` |
| `ILineageStore` | `interface` | Persistent birth/death log. Records survive cell death. Default: `InMemoryLineageStore`. | `src/Ananke.Organics/Kernel/Lineage/ILineageStore.cs` |
| `HostSnapshot` | `sealed record` | Point-in-time capture of the entire mesh topology (all cells, routing table, version). Supports rollback, diff, audit, and cross-deployment bootstrap. | `src/Ananke.Organics/Kernel/Snapshots/HostSnapshot.cs` |

---

## Lifecycle Model

### Normal execution (no division)

```
workflow.JoinHost(host, toolKit)          → OrganicWorkflow<TState>
OrganicWorkflow.RunAsync(state)
  → inner Workflow.RunAsync(state)        (unmodified)
  → host.ObserveExecution(name, result)
      → WorkflowExecutionMonitor records execution
      → every N executions: evaluate IDivisionPolicy
          → null: nothing to do
          → DivisionPlan: continue to approval
```

### Division flow

```
IDivisionOutcomeTracker.RecordBaseline(divisionId, snapshot)   ← before evaluating the plan

IDivisionPolicy.EvaluateAsync(snapshot, manifest)
  → DivisionPlan (proposed children + cluster strategy)

IDivisionApprovalGate.ReviewAsync(plan, snapshot)
  → DivisionApproval.Approved / Rejected / Revised

IWorkflowDivider.DivideAsync(plan, approval)
  → derive child snapshots (WorkflowSnapshotBuilder)
  → seed each child's empirical memory (DomainAffinityMemory)
  → activate each child workflow (IWorkflowActivatorFactory)
  → IWorkflowHost.StartAsync(child) × N
  → confirm all children alive
  → IWorkflowHost.StopAsync(parent)
  → ILineageStore.RecordBirthAsync(child) × N
  → ILineageStore.RecordDeathAsync(parent)

IDivisionOutcomeTracker.RewardAsync(divisionId, childSnapshots, originalPlan)
  ← later, once children have accumulated enough post-division executions;
    compares child ComplexitySnapshot entries to the recorded baseline and
    reinforces/contradicts the empirical entries that influenced the plan
```

If any child fails to start → all spawned children are torn down, parent survives.

### Healing flow

`IHealthMonitor` and the `IHealingPolicy` strategies are implemented and unit-tested, but
`OrganicHost` does not yet drive this loop automatically on a tick — evaluation is
caller-driven today (see `OrganicGrowthOptions.ApoptosisPolicy` remarks for the intended
background-loop wiring):

```
IHealthMonitor.GetSnapshotAsync(name)        → ComplexitySnapshot
IHealthMonitor.GetHealthSnapshotAsync(name)  → HealthSnapshot? (null if insufficient data)

IHealingPolicy.EvaluateAsync(health, complexity)
  → null: cell healthy
  → HealingPlan { Strategy }:
      HealingStrategy.Restart  → IWorkflowHost.StopAsync + StartAsync
      HealingStrategy.Rollback → restore prior HostSnapshot, respawn
      HealingStrategy.Prune    → AgedCellPrunePolicy / IdleCellPrunePolicy removes cell
```

### Sensing and routing

```
Cell heartbeat → ICapabilityMap.RegisterAsync(cellId, domains, tools)
                → IMeshAggregator.Report(cellId, MetabolicSignal)

Incoming message → IRequestRouter.RouteAsync(userMessage)
  → ICapabilityMap: find domain match(es)
  → RoutingAffinityTracker: weight by historical affinity
  → returns cell name for dispatch
```

---

## Division Policies

| Policy | Cold-start? | Description |
|---|---|---|
| `ThresholdDivisionPolicy` | ✅ Yes | Triggers when `ToolCount ≥ minTools` AND `TagClusterCount ≥ minClusters`. Splits using an injected `clusterStrategy` or falls back to an even two-way split. |
| `ExperienceDrivenDivisionPolicy` | ❌ Warm only | Recalls past `DivisionExperience` entries from `IEmpiricalMemory`. Uses UCB exploration to choose between proven and novel split strategies. Falls back to `ThresholdDivisionPolicy` on cold start. |

## Approval Gates

| Gate | Description |
|---|---|
| `AutoApprovalGate` | Approves all plans immediately. Default; preserves fully-automatic behavior. |
| `LlmApprovalGate` | Forwards the plan to an `IAgentModel` for autonomous review. Returns `Approved`, `Rejected`, or `Revised` with LLM rationale. |
| `CallbackApprovalGate` | Human-in-the-loop — invokes a delegate (Slack, Teams, email, web UI, etc.). |
| `MetabolicDivisionApprovalGate` | Approves only when the current `MeshSignal` stress ratio is below a threshold (prevents division under load). |

## Healing Policies

| Policy | Description |
|---|---|
| `ThresholdHealingPolicy` | Requires N consecutive error-rate windows above threshold before triggering. Distinguishes restart (latency rising) vs rollback (latency flat). Skips cells above a complexity ceiling — divide first. |
| `CompositeHealingPolicy` | Chains multiple `IHealingPolicy` instances; first non-null plan wins. |
| `AgedCellPrunePolicy` | Prunes cells that have been alive longer than a configured TTL with no recent executions. |
| `IdleCellPrunePolicy` | Prunes cells below a minimum execution rate threshold. |

---

## Snapshots and Restoration

`HostSnapshot` captures the full mesh topology at a point in time:
- All cells with their `WorkflowSnapshot` (manifest, tools, domains, memory, routing)
- Domain → cell routing table
- Monotonic version counter

`HostSnapshotExporter` serialises to/from YAML for storage, cross-deployment bootstrap, diffing, and audit.

`WorkflowSnapshotBuilder` provides a fluent builder for constructing `WorkflowSnapshot` entries, with sensible defaults for single-agent cells and division children (lineage, memory seeding, model alias).

`PromptWorkflowDesigner` uses `Ananke.Design` to grow a new child workflow from an LLM-generated manifest spec — enabling fully autonomous topology evolution.

---

## Lineage

`ILineageStore` records every cell birth and death. Records are **never deleted** — the store is append-only with logical tombstones. This supports:

- `GetDescendantsAsync(cellId)` — full recursive genealogy tree
- `GetByGenerationAsync(n)` — topology at a specific evolutionary generation
- Post-mortem audit of division chains

Default: `InMemoryLineageStore` (survives restarts only if the host process does). Production deployments should implement `ILineageStore` backed by a persistent store (Redis, Postgres, etc.).

---

## Async Review Parking

`CallbackWorkReviewGate` can stall a workflow thread for hours while a reviewer sleeps.
`ParkingCallbackWorkReviewGate` solves this with a park-and-resume pattern:

```
Workflow calls ReviewAsync(item)
  → item is persisted via IWorkReviewParkingStore (returns opaque parkingId)
  → WorkReviewDecision { Outcome = Pending, Comment = parkingId } is returned immediately
  → workflow runner checkpoints and yields the thread

Reviewer clicks Approve / Revise / Reject (e.g. via SlackApprovalCallback)
  → ParkingCallbackWorkReviewGate.ResumeAsync(parkingId, decision) is called
  → store entry is removed (CompleteAsync)
  → any in-process waiter on the original ReviewAsync call is resolved
  → workflow runner re-enters the gate with the real decision
```

The `Comment` field of the `Pending` decision carries the opaque parking id so the
calling code can forward it into a Slack message (or any other channel) without needing a
separate out-parameter.

### Implementations

| Type | Description |
|---|---|
| `IWorkReviewParkingStore` | Store interface — `ParkAsync`, `TryGetAsync`, `CompleteAsync`. |
| `InMemoryWorkReviewParkingStore` | Default in-process implementation. Thread-safe; state is lost on restart. |
| `ParkingCallbackWorkReviewGate` | Gate implementation. Exposes `ReviewAsync` (parks) and `ResumeAsync` (resolves). |

### Typical wiring with Slack

1. Workflow calls `ReviewAsync` — receives `Pending` + `parkingId`.
2. Application posts `SlackApprovalBlocks.Build(workItem)` to the review channel, embedding the `parkingId` in the message metadata or block value.
3. Reviewer clicks a button → `SlackApprovalCallback.HandleInteractionAsync` resolves the decision.
4. Application calls `gate.ResumeAsync(parkingId, decision)`.
5. Workflow continues with the resolved `WorkReviewDecision`.

---

## Extension Points

| Interface | Default | Purpose |
|---|---|---|
| `IWorkflowHost` | `InProcessWorkflowHost` | Cell lifecycle (spawn / kill) — swap for Docker, K8s, bare-metal |
| `IDivisionPolicy` | `ThresholdDivisionPolicy` | When and how to divide |
| `IDivisionApprovalGate` | `AutoApprovalGate` | Governance before execution |
| `IWorkflowDivider` | `WorkflowDivider` | Division execution strategy |
| `IDivisionTransition` | `StopTheWorldDivisionTransition` | Concurrency model during transition (stop-the-world or rolling) |
| `IHealingPolicy` | `ThresholdHealingPolicy` | When and how to heal |
| `IHealthMonitor` | `WorkflowExecutionMonitor` | Health metrics source |
| `ICapabilityMap` | `InMemoryCapabilityMap` | Domain / capability registry |
| `IMeshAggregator` | `InMemoryMeshAggregator` | Mesh-wide stress aggregation |
| `IRequestRouter` | `KeywordRequestRouter` | Request → cell dispatch |
| `ILineageStore` | `InMemoryLineageStore` | Birth/death persistence |
| `IWorkReviewParkingStore` | `InMemoryWorkReviewParkingStore` | Pending-review state storage — swap for Redis, Postgres, etc. for durability |
| `IWorkflowActivatorFactory` | `TypedWorkflowActivatorFactory` | Constructs new child workflows from snapshots |
| `IDivisionOutcomeTracker` | `DivisionOutcomeTracker` | Records division results back to empirical memory |

---

## Persistence Assumptions

All default implementations (`InMemory*`) are in-process only.
A process restart loses:
- capability map registrations
- mesh aggregator state
- lineage records
- division outcome history
- routing affinity scores
- parked review state (use a durable `IWorkReviewParkingStore` to survive restarts)

Production deployments must supply persistent implementations of `ILineageStore`,
`ICapabilityMap`, and `IEmpiricalMemory` (from `Ananke.Learning`).
`HostSnapshot` YAML export can checkpoint mesh topology for bootstrap on restart.

---

## Public API Stability

| Surface | Stability |
|---|---|
| `OrganicHost`, `OrganicWorkflow<TState>`, `OrganicWorkflowExtensions.JoinHost` | Stable |
| `IWorkflowHost` / `InProcessWorkflowHost` / `IWorkflowReplicator` | Stable |
| `IDivisionPolicy` / `IDivisionApprovalGate` / `IWorkflowDivider` | Stable |
| `ThresholdDivisionPolicy` / `ExperienceDrivenDivisionPolicy` | Stable |
| `AutoApprovalGate` / `CallbackApprovalGate` | Stable |
| `IHealingPolicy` / `ThresholdHealingPolicy` / `CompositeHealingPolicy` | Stable |
| `ICapabilityMap` / `IRequestRouter` / `IMeshAggregator` | Stable |
| `ILineageStore` / `InMemoryLineageStore` / `CellLineage` | Stable |
| `HostSnapshot` / `WorkflowSnapshotBuilder` / `HostSnapshotExporter` | Stable |
| `OrganicGrowthOptions` / `OrganicGrowthOptionsBuilder` | Stable |
| `IWorkReviewGate` / `WorkItem` / `WorkReviewDecision` / `WorkReviewOutcome` | Stable |
| `IWorkReviewParkingStore` / `InMemoryWorkReviewParkingStore` / `ParkingCallbackWorkReviewGate` | Stable |
| `LlmApprovalGate` / `MetabolicDivisionApprovalGate` | **Preview** — LLM gate prompt format may change |
| `PromptWorkflowDesigner` | **Preview** — autonomous topology evolution is experimental |
| `QuorumApprovalGate` | **Preview** |
| `IRemoteCellSource` / federated division path | **Preview** — federated host integration is not yet complete |

Breaking changes to **Stable** surfaces require a documented design review.
