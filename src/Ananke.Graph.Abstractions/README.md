# Ananke.Graph.Abstractions

[![NuGet](https://img.shields.io/nuget/v/Ananke.Graph.Abstractions.svg)](https://www.nuget.org/packages/Ananke.Graph.Abstractions)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

Shared connection options for `Ananke.Graph.*` backend adapter packages.

This package is intentionally narrow. It does **not** define the graph domain model itself; the graph contracts (`IKnowledgeGraph`, `GraphNode`, `GraphEdge`) live in `Ananke.Abstractions.Graph`. `Ananke.Graph.Abstractions` only provides backend-facing connection settings reused by graph adapter packages.

## Install

```bash
dotnet add package Ananke.Graph.Abstractions
```

Most applications will not install this package directly; they will consume it transitively through a concrete backend package.

## What is included

| Type | Description |
|---|---|
| `GraphConnectionOptions` | Shared Bolt-style connection settings used by graph adapters such as Memgraph/Neo4j-compatible providers |

`GraphConnectionOptions` includes:

- `Uri` — graph server endpoint such as `bolt://localhost:7687`
- `Username` / `Password` — optional credentials
- `Database` — optional logical database name for drivers that support it
- `MaxConnectionPoolSize` — connection-pool cap
- `MaxConnectionLifetime` — optional pooled-connection lifetime override

## Relationship to other graph packages

| Package | Role |
|---|---|
| `Ananke.Abstractions` | Defines the graph contracts in `Ananke.Abstractions.Graph` |
| `Ananke.Graph.Abstractions` | Defines backend connection options shared by graph adapters |
| `Ananke.Graph.Memgraph` | Concrete Memgraph / Bolt-backed implementation |

## Usage

Register `GraphConnectionOptions` from configuration and pass it to the backend-specific factory or graph implementation.

```csharp
services.Configure<GraphConnectionOptions>(config.GetSection("Graph"));
services.AddSingleton<MemgraphSessionFactory>();
services.AddSingleton<IKnowledgeGraph, MemgraphKnowledgeGraph>();
```

## Documentation

Full docs and package guidance: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
