# Ananke.Postgres

[![NuGet](https://img.shields.io/nuget/v/Ananke.Postgres.svg)](https://www.nuget.org/packages/Ananke.Postgres)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

PostgreSQL provider for Ananke: `PostgresKnowledgeStore` (`IKnowledgeStore`) and `PostgresKnowledgeCatalog`
(`IKnowledgeCatalog`), on pgvector for embedding search and Postgres full-text search for keywords. Experimental
(`ANNKEEXP004`).

## Install

```bash
dotnet add package Ananke.Postgres
```

Needs Postgres with the [pgvector](https://github.com/pgvector/pgvector) extension available. Quickest way:

```bash
docker run -p 5432:5432 -e POSTGRES_PASSWORD=postgres pgvector/pgvector:pg17
```

## Quick start

```csharp
#pragma warning disable ANNKEEXP004
using Ananke.Orchestration.Knowledge;
using Ananke.Postgres;
using Npgsql;

await using var dataSource = NpgsqlDataSource.Create("Host=localhost;Username=postgres;Password=postgres");
var store = new PostgresKnowledgeStore(dataSource, embedder, new PostgresKnowledgeOptions { Table = "docs_chunks" });

await store.UpsertAsync([new KnowledgeDocument { Id = "0001#0", Text = "Core.Identity is built on OpenIddict" }]);

var dense = await store.SearchAsync("token server library");
var keywords = await store.SearchAsync("OpenIddict", new SearchOptions { Mode = SearchMode.Sparse });
var both = await store.SearchAsync("Which library is Core.Identity built on?", new SearchOptions { Mode = SearchMode.Hybrid });
```

- **Search modes.** `Dense` ranks by cosine distance (HNSW index). `Sparse` matches any of the query's words in a
  generated `tsvector` column (GIN index), ranked by `ts_rank_cd`. `Hybrid` fuses both by reciprocal rank.
- **Metadata** is `jsonb`; a `KnowledgeFilter` matches rows whose metadata contains every key and value given.
- **Deletes remove rows.** `DeleteAsync` with an empty filter empties the table.
- **One embedder per table.** The table is stamped with the embedder's `Identity` and vector length in
  `ananke_embedders`; a store whose embedder differs is refused, naming both.
- **Created on first use:** the `vector` extension, the table and its indexes. The role needs the rights to create
  them, or they must already exist.

## Options

| Option | Default | What it does |
|---|---|---|
| `Table` | `ananke_knowledge` (`ananke_catalog` for the catalog) | The table, optionally schema-qualified; lower-case letters, digits, underscores |
| `TextSearchConfiguration` | `simple` | How keyword search reads text: `simple` keeps words as they are; `english` stems them |
| `Dimensions` | from the embedder | The vectors' length, when the embedder's identity doesn't say |
