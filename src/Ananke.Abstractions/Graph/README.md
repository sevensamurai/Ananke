# Ananke.Abstractions.Graph

A small, zero-dependency typed graph used by `Ananke.Learning` and `Ananke.Organics` to
project tag/episode/cell structure into a queryable shape.

---

## Why it is hand-written

We evaluated existing .NET graph libraries before writing this substrate:

| Library | Last release | Verdict |
|---|---|---|
| **QuikGraph 2.5.0** | July 2022 | No active maintenance; MS-PL; oversized API surface for our needs. |
| **YC.QuickGraph** | 2019 | Abandoned. |
| **Satsuma** | — | Never on NuGet. |
| **MSAGL** | — | Graph *layout*, not graph algorithms. |

None of these met the maintenance, licensing, AOT-friendliness, or surface-area bar this
project enforces.  The substrate is intentionally small (~300 LOC) and implements only the
algorithms `Ananke.Learning` and `Ananke.Organics` consume: BFS, degree centrality, and
PageRank.

---

## What is in scope

| Feature | Type |
|---|---|
| Typed nodes and edges with provenance (`Extracted / Inferred / Ambiguous`) | `GraphNode`, `GraphEdge`, `EdgeProvenance` |
| Upsert semantics: weight `max`; provenance only promotes | `IKnowledgeGraph` contract |
| k-hop BFS expansion with node budget | `IKnowledgeGraph.ExpandAsync` |
| Neighbour traversal (in + out edges, optional relation filter) | `IKnowledgeGraph.NeighborsAsync` |
| Default in-memory backend | `InMemoryKnowledgeGraph` |
| Degree centrality | `DegreeCentralityScorer` |
| Iterative PageRank | `PageRankCentralityScorer` |
| Community detection interface (no default implementation in v1) | `ICommunityDetector` |

---

## What is out of scope

A\*, max-flow, MST, k-shortest-path, topological sort, Cypher-style queries, persistent
storage, graph layout.  Add via a backend-specific implementation of `IKnowledgeGraph` if
needed.

---

## Getting started

```csharp
IKnowledgeGraph graph = new InMemoryKnowledgeGraph();

await graph.UpsertNodeAsync(new GraphNode { Id = "tag:cause/gc-pause", Kind = "tag" });
await graph.UpsertNodeAsync(new GraphNode { Id = "tag:cause/oom", Kind = "tag" });

await graph.UpsertEdgeAsync(new GraphEdge
{
    FromId     = "tag:cause/gc-pause",
    ToId       = "tag:cause/oom",
    Relation   = "co_occurs",
    Provenance = EdgeProvenance.Inferred,
    Weight     = 0.7f,
});

var nodes = await graph.ExpandAsync(["tag:cause/gc-pause"], hops: 2, maxNodes: 50);
```

---

## How to extend

Implement `IKnowledgeGraph` for a Neo4j, Kùzu, or Qdrant-payload-edges backend.
Everything else — builders, analytics, exporters — stays unchanged because they depend
only on the interface.
