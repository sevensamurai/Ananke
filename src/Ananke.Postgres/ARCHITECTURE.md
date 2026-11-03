# Ananke.Postgres — Architecture

> PostgreSQL provider — knowledge store and knowledge catalog on pgvector and full-text search.

## Role

Durable `IKnowledgeStore` and `IKnowledgeCatalog` for hosts that already run Postgres. Depends on
`Ananke.Abstractions`, `Ananke.Orchestration.Knowledge` and Npgsql; vectors are passed as text and cast with
`::vector`, so no pgvector client package is needed.

## Start Here

1. `PostgresKnowledgeStore` — chunks: `Dense`, `Sparse` and `Hybrid` search, `jsonb` filters, deletes —
   `src/Ananke.Postgres/PostgresKnowledgeStore.cs`
2. `PostgresKnowledgeCatalog` — one row per source document; discovery leaves out superseded entries —
   `src/Ananke.Postgres/PostgresKnowledgeCatalog.cs`
3. `PostgresSchema` (internal) — creates tables and indexes on first use, under an advisory lock, and stamps each table
   with its embedder in `ananke_embedders` — `src/Ananke.Postgres/PostgresSchema.cs`
4. `PostgresKnowledgeOptions` — table, text search configuration, dimensions

## Tables

| Table | Columns | Indexes |
|---|---|---|
| store (`ananke_knowledge`) | `id` (key), `text`, `metadata` jsonb, `tsv` (generated from `text`), `embedding` vector(n) | HNSW cosine on `embedding`, GIN on `tsv`, GIN `jsonb_path_ops` on `metadata` |
| catalog (`ananke_catalog`) | `source` (key), `summary`, `keywords` text[], `category`, `indexed_at`, `chunk_count`, `superseded_by`, `embedding` | HNSW cosine on `embedding` |
| `ananke_embedders` | `table_name` (key), `embedder`, `dimensions` | — |

Table and configuration names are checked against a plain-identifier pattern before they reach SQL; every value is a
parameter.
