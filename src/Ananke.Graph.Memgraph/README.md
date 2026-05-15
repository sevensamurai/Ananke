# Ananke.Graph.Memgraph

Persistent [`IKnowledgeGraph`](../Ananke.Abstractions/Graph/IKnowledgeGraph.cs)
implementation backed by **[Memgraph](https://memgraph.com/)** via the Neo4j Bolt
protocol.

## Requirements

- Memgraph ≥ 2.x running and reachable over Bolt (default port **7687**).
- Optional: [MAGE](https://memgraph.com/mage) installed for hardware-accelerated
  PageRank and community-detection overrides.

## Quick start

```csharp
// appsettings.json / IConfiguration
// "Graph": { "Uri": "bolt://localhost:7687", "Username": "memgraph", "Password": "" }

services.Configure<GraphConnectionOptions>(config.GetSection("Graph"));
services.AddSingleton<MemgraphSessionFactory>();
services.AddSingleton<IKnowledgeGraph, MemgraphKnowledgeGraph>();

// Optional MAGE algorithm overrides:
// services.AddSingleton<ICentralityScorer, MemgraphPageRankScorer>();
// services.AddSingleton<ICommunityDetector, MemgraphCommunityDetector>();
```

## Docker (development)

```bash
docker run -d -p 7687:7687 --name memgraph memgraph/memgraph
# with MAGE:
docker run -d -p 7687:7687 --name memgraph memgraph/memgraph-mage
```

## License

Apache 2.0 — see repository root.
