# Ananke.Graph.Abstractions — Architecture

> Shared connection configuration for `Ananke.Graph.*` backend adapters.

## Role

A minimal, zero-logic package holding the connection options type shared by every
`IKnowledgeGraph` backend implementation (currently `Ananke.Graph.Memgraph`; future
backends such as a Neo4j adapter would depend on this package instead of duplicating
connection settings).

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `GraphConnectionOptions` — the package's sole type; Bolt URI, optional
   username/password/database, connection-pool size, and max connection lifetime —
   passed to a backend's session factory — `src/Ananke.Graph.Abstractions/GraphConnectionOptions.cs`

---

## Dependencies

- `Ananke.Abstractions` (project)

## Key Types

| Type | Kind | Purpose | Source |
|------|------|---------|--------|
| `GraphConnectionOptions` | Sealed record | Bolt URI, optional username/password/database, connection-pool size, and max connection lifetime — passed to a backend's session factory | `src/Ananke.Graph.Abstractions/GraphConnectionOptions.cs` |

## Why a separate package

`IKnowledgeGraph` itself lives in `Ananke.Abstractions.Graph` (zero-dependency contracts).
Connection configuration is split out here, one level up, so that backend packages
(`Ananke.Graph.Memgraph` and any future graph adapter) share one options type without
forcing `Ananke.Abstractions` to know about Bolt-protocol connection concepts.
