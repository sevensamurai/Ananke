# Ananke.Graph.Memgraph — Architecture

> `IKnowledgeGraph` backend for Memgraph (Bolt protocol).

## Role

Implements the graph-substrate contracts from `Ananke.Abstractions.Graph` against a
Memgraph server, using the Neo4j Bolt driver (Memgraph speaks the Bolt protocol).
Used by `Ananke.Organics.Topology` (colony graph) and `Ananke.Learning.Knowledge`
(empirical-memory graph projections) when a persistent, queryable graph store is
needed instead of the in-memory default.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `MemgraphKnowledgeGraph` — the `IKnowledgeGraph` implementation — multi-label node
   MERGE/SET, edge upsert with provenance, Cypher-backed queries — `src/Ananke.Graph.Memgraph/MemgraphKnowledgeGraph.cs`
2. `MemgraphSessionFactory` — `IAsyncDisposable` owner of the Bolt driver instance; opens
   sessions from `GraphConnectionOptions` — `src/Ananke.Graph.Memgraph/MemgraphSessionFactory.cs`
3. `MemgraphPageRankScorer` — `ICentralityScorer` implementation; runs Memgraph's PageRank
   MAGE module — `src/Ananke.Graph.Memgraph/MemgraphPageRankScorer.cs`
4. `MemgraphCommunityDetector` — `ICommunityDetector` implementation; runs Memgraph's
   community-detection MAGE module — `src/Ananke.Graph.Memgraph/MemgraphCommunityDetector.cs`

---

## Dependencies

- `Ananke.Abstractions` (project)
- `Ananke.Graph.Abstractions` (project) — `GraphConnectionOptions`
- `Neo4j.Driver` (NuGet — Bolt protocol client)
- `Microsoft.Extensions.Options`

## Key Types

| Type | Kind | Purpose | Source |
|------|------|---------|--------|
| `MemgraphSessionFactory` | Sealed class | `IAsyncDisposable` — owns the Bolt driver instance and opens sessions from `GraphConnectionOptions` | `src/Ananke.Graph.Memgraph/MemgraphSessionFactory.cs` |
| `MemgraphKnowledgeGraph` | Sealed class | `IKnowledgeGraph` implementation — multi-label node MERGE/SET, edge upsert with provenance, Cypher-backed queries | `src/Ananke.Graph.Memgraph/MemgraphKnowledgeGraph.cs` |
| `MemgraphPageRankScorer` | Sealed class | `ICentralityScorer` implementation — runs Memgraph's PageRank MAGE module, with optional node-label filtering | `src/Ananke.Graph.Memgraph/MemgraphPageRankScorer.cs` |
| `MemgraphCommunityDetector` | Sealed class | `ICommunityDetector` implementation — runs Memgraph's community-detection MAGE module | `src/Ananke.Graph.Memgraph/MemgraphCommunityDetector.cs` |

## Notes

- All node/edge writes go through parameterized Cypher (no string-built queries).
- `MemgraphKnowledgeGraph` stores `GraphNode.Kind` as both a node property and an actual
  Cypher label when `Labels`/`EffectiveLabels` are populated, enabling label-scoped `MATCH`
  queries from PageRank/community detection without a full graph scan.
