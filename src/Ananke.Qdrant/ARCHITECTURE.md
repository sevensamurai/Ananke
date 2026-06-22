# Ananke.Qdrant — Architecture

> Qdrant vector database provider — knowledge store, empirical memory,
> episode store, and knowledge catalog backed by Qdrant.

## Role

Provides vector-search-backed implementations of Ananke's knowledge and
learning abstractions. Handles collection management, dense vector upsert/search,
and metadata filtering.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `QdrantKnowledgeStore` — `IKnowledgeStore` implementation; vector knowledge storage with
   similarity search — `src/Ananke.Qdrant/QdrantKnowledgeStore.cs`
2. `QdrantEmpiricalMemory` — `IEmpiricalMemory` implementation; empirical pattern storage
   with vector search — `src/Ananke.Qdrant/QdrantEmpiricalMemory.cs`
3. `QdrantEpisodeStore` — `IEpisodeStore` implementation; episode persistence with vector
   indexing — `src/Ananke.Qdrant/QdrantEpisodeStore.cs`
4. `QdrantKnowledgeCatalog` — `IKnowledgeCatalog` implementation; document catalog with
   vector-based discovery — `src/Ananke.Qdrant/QdrantKnowledgeCatalog.cs`

---

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
