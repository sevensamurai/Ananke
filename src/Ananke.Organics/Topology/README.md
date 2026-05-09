# Ananke.Organics — Topology slice

This folder projects the live organic mesh into a typed knowledge graph so that
structural patterns (god nodes, lineage depth, routing imbalances) become
queryable and reportable without changing any operational components.

## What's here

| File | Responsibility |
|---|---|
| `ColonyGraphBuilder` | Reads `ICapabilityMap`, `ILineageStore`, and optional `RoutingAffinityTracker` observations; writes cell/domain/tool nodes and relationship edges into an `IKnowledgeGraph`. |
| `Centrality/GodNodeDetector` | Consumes an `ICentralityScorer` over the colony graph and returns the top-k cells whose degree centrality exceeds a configurable threshold. |
| `Reporting/ColonyReportExporter` | Serializes the colony graph to `colony.json` and writes a human-readable `COLONY_REPORT.md` summary. |

## Node / edge vocabulary

**Nodes**

| Kind | ID format |
|---|---|
| `cell` | `cell:{CellId}` |
| `domain` | `domain:{name}` |
| `tool` | `tool:{kit}/{name}` |

**Edges**

| Relation | Direction | Provenance |
|---|---|---|
| `descended_from` | cell → cell | Extracted |
| `serves` | cell → domain | Extracted |
| `routed_to` | domain → cell | Inferred (affinity) / Ambiguous (keyword) |
| `co_failed` | cell ↔ cell | Inferred |

## Usage

```csharp
var graph = new InMemoryKnowledgeGraph();
var builder = new ColonyGraphBuilder(capabilityMap, lineageStore);
await builder.BuildAsync(graph);

var scorer = new DegreeCentralityScorer();
var detector = new GodNodeDetector(scorer) { TopK = 3, Threshold = 0.4f };
var gods = await detector.DetectAsync(graph);

var exporter = new ColonyReportExporter();
await exporter.ExportAsync(graph, gods, outputDirectory);
```
