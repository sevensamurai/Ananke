# Ananke.Qdrant — Architecture

> Qdrant vector database provider — knowledge store, empirical memory,
> episode store, and knowledge catalog backed by Qdrant.

## Role

Provides vector-search-backed implementations of Ananke's knowledge and
learning abstractions. Handles collection management, dense vector upsert/search,
and metadata filtering.

## Dependencies

- `Ananke.Orchestration` (project)
- `Ananke.Learning` (project)
- `Qdrant.Client`

## Key Types

| Type | Kind | Purpose |
|------|------|---------|
| `QdrantKnowledgeStore` | Class | `IKnowledgeStore` — vector knowledge storage with similarity search |
| `QdrantEmpiricalMemory` | Class | `IEmpiricalMemory` — empirical pattern storage with vector search |
| `QdrantEpisodeStore` | Class | `IEpisodeStore` — episode persistence with vector indexing |
| `QdrantKnowledgeCatalog` | Class | `IKnowledgeCatalog` — document catalog with vector-based discovery |
