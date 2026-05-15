# Ananke.Graph.Abstractions

Shared connection options and helpers for **Ananke graph-backend adapter packages**.

This package does not contain a graph implementation itself — it provides the
common types (e.g. `GraphConnectionOptions`) reused by every `Ananke.Graph.*`
backend:

| Package | Backend |
|---|---|
| `Ananke.Graph.Memgraph` | [Memgraph](https://memgraph.com/) via Neo4j Bolt |
| `Ananke.Graph.Age` *(planned)* | [Apache AGE](https://age.apache.org/) on PostgreSQL |

## Usage

Register `GraphConnectionOptions` in your DI container and pass it to the
backend-specific session factory (e.g. `MemgraphSessionFactory`).

```csharp
services.Configure<GraphConnectionOptions>(config.GetSection("Graph"));
services.AddSingleton<MemgraphSessionFactory>();
services.AddSingleton<IKnowledgeGraph, MemgraphKnowledgeGraph>();
```

## License

Apache 2.0 — see repository root.
