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

- **Search modes.** `Dense` ranks by cosine distance (HNSW index). `Sparse` searches a generated `tsvector` column
  (GIN index) and ranks by BM25. `Hybrid` fuses both by reciprocal rank.
- **Keyword search ranks by BM25 from the table's own counts.** How many rows hold each lexeme, and the table's rows
  and length, are kept in `<table>_lexemes` by triggers on the table, so a word common in your documents counts
  little, in any language and with no stop list (k1 1.2, b 0.75, as `InMemoryKnowledgeStore`). Only rows holding a
  rare query term, one found in at most `RareTermShare` of rows (5%), are scored; a query with no rare term requires
  all its terms. So *what is the telemetry backend* scores the rows holding `telemetry` or `backend`, not every row
  holding *the*. The counts are over the whole table, not the rows a filter keeps.
- **Writes to one table run one at a time** (a transaction-level advisory lock), so the counts stay right and two
  writes can't deadlock. Upsert writes a batch in one statement; the last document given for an id is kept.
- **Dense search returns the rows asked, under a filter too.** pgvector reads at most `hnsw.ef_search` candidates
  from the HNSW index (40 by default) and applies the filter and the score threshold after. So each dense query (and
  the catalog's `DiscoverAsync`, which leaves out superseded entries) runs in a transaction of its own with
  `SET LOCAL hnsw.ef_search` raised to the rows asked (up to 1,000, pgvector's limit) and, on pgvector 0.8 or later,
  `SET LOCAL hnsw.iterative_scan = strict_order`, so a filtered scan reads on until it has the rows. The pgvector
  version is read once, when the store first uses its table. An iterative scan stops after `hnsw.max_scan_tuples`
  (20,000 by default, a server setting); on an earlier pgvector a filter that keeps few rows can still return fewer
  than asked.
- **Metadata** is `jsonb`; a `KnowledgeFilter` matches rows whose metadata contains every key and value given, and
  for each key in `KnowledgeFilter.AnyOf` any one of its values.
- **Lists by date in the query.** `ListAsync` with a `KnowledgeDateOrder` casts the key's value to `timestamptz`,
  orders newest first and applies the lower bound and the limit in SQL; a row whose value isn't a date and time
  (`pg_input_is_valid`, PostgreSQL 16 or later) comes after the dated ones. Without an order it lists by id.
- **Deletes remove rows.** `DeleteAsync` with an empty filter empties the table.
- **One embedder per table.** The table is stamped with the embedder's `Identity` and vector length in
  `ananke_embedders`; a store whose embedder differs is refused, naming both.
- **Created on first use:** the `vector` extension, the table and its indexes, and `<table>_lexemes` with its
  trigger function and triggers. The role needs the rights to create them, or they must already exist. A table made
  by an earlier version gets its lexeme counts, from `ts_stat`, the first time a store opens it; writers wait while
  they are filled.

## Options

| Option | Default | What it does |
|---|---|---|
| `Table` | `ananke_knowledge` (`ananke_catalog` for the catalog) | The table, optionally schema-qualified; lower-case letters, digits, underscores |
| `TextSearchConfiguration` | `simple` | How keyword search reads text: `simple` keeps words as they are; `english` stems them |
| `RareTermShare` | 0.05 | Keyword search scores only rows holding a query term found in at most this share of rows (rounded up to whole rows); with none, rows holding every term. 1 scores every row holding any term |
| `Dimensions` | from the embedder | The vectors' length, when the embedder's identity doesn't say |
| `HnswEfSearch` | 40 | The least `hnsw.ef_search` for dense search; a search asking for more rows raises it to the rows asked, up to 1,000 |
| `HnswIterativeScan` | `StrictOrder` | `hnsw.iterative_scan` for dense search on pgvector 0.8 or later: `StrictOrder` or `Off` |
