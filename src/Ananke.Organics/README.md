# Ananke.Organics

Organic kernel architecture for Ananke - self-organizing workflow ecosystems
with cell division, heartbeat sensing, triage routing, domain-affine memory,
and evolutionary division policies.

## What Is This?

Workflows are **cells** in a living kernel. They grow organically as they
accumulate tools and knowledge, and when they grow too complex, they
**divide** - the parent dies and two specialized peers emerge. The kernel
is the organism; cells are mortal.

## Key Concepts

| Concept | Type | Description |
|---|---|---|
| Kernel | IWorkflowHost | Manages cell lifecycle (spawn, kill, list) |
| Nervous System | ICapabilityMap | Aggregates heartbeat signals into a live capability map |
| Triage Router | IRequestRouter | Routes requests to the right cell |
| Complexity Monitor | IComplexityMonitor | Measures structural surface tension |
| Division Policy | IDivisionPolicy | Decides when and how to divide |
| Cell Divider | IWorkflowDivider | Executes the actual division |
| Cell Replicator | IWorkflowReplicator | Clones a cell for scaling (original lives) |
| Domain Affinity | DomainAffinityMemory | Biases memory recall toward a cell's domain |

## Package Structure

Ananke.Organics/
  Kernel/     - IWorkflowHost, InProcessWorkflowHost, lifecycle events, IWorkflowReplicator
  Sensing/    - WorkflowSignal, ICapabilityMap, IRequestRouter
  Division/   - ComplexitySnapshot, IDivisionPolicy, IWorkflowDivider, DomainAffinityMemory
  Healing/    - IHealingPolicy, ThresholdHealingPolicy, HealthSnapshot
  Topology/   - ColonyGraphBuilder, GodNodeDetector, ColonyReportExporter

## Topology — Colony graph and reporting

The `Topology/` slice projects the live mesh state into an `IKnowledgeGraph`
(from `Ananke.Abstractions.Graph`) so that structural patterns become queryable
and reportable without changing any operational component.

| Type | Description |
|---|---|
| `ColonyGraphBuilder` | Reads `ICapabilityMap`, `ILineageStore`, and optional `RoutingAffinityTracker`; writes cell/domain/tool nodes and relationship edges |
| `GodNodeDetector` | Returns top-k cells by degree centrality that exceed a configurable threshold |
| `ColonyReportExporter` | Writes `colony.json` (machine-readable snapshot) and `COLONY_REPORT.md` (operator summary) |

See [`Topology/README.md`](./Topology/README.md) for node/edge vocabulary and usage examples.
