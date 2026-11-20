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
   with its embedder in `ananke_embedders`; reads the pgvector version once, and runs each nearest-neighbour query
   (`NearestAsync`) in a transaction with its HNSW settings — `src/Ananke.Postgres/PostgresSchema.cs`
4. `PostgresKnowledgeOptions` — table, text search configuration, `RareTermShare`, dimensions, `HnswEfSearch`,
   `HnswIterativeScan`

## Dense search

pgvector's HNSW index reads at most `hnsw.ef_search` candidates (40 by default) and the query's `WHERE` (a metadata
filter, the score threshold, `superseded_by IS NULL`) is applied to those after, so without more a dense query
returns fewer rows than asked. `PostgresSchema.NearestAsync` runs the query in a transaction of its own, after
`SET LOCAL hnsw.ef_search` = the rows asked, at least `HnswEfSearch` and at most 1,000, and, when the installed pgvector
is 0.8 or later (read from `pg_extension` in `EnsureAsync`), `SET LOCAL hnsw.iterative_scan` = `strict_order` or `off`
from `HnswIterativeScan`. Both the store's `Dense` and `Hybrid` searches and the catalog's `DiscoverAsync` go through
it. The store applies its score threshold outside the scan, to the nearest rows the filter keeps, so an iterative
scan doesn't read on for rows the threshold would drop. A query the planner answers without the HNSW index (a small
table, or a filter narrow enough for the GIN index) is exact, and the settings don't change it.

## Keyword search

`ts_rank_cd` uses no statistics of the table, so a word in every row counts as much as a rare one. The store ranks by
BM25 instead, computed in SQL from counts it keeps:

- **`<table>_lexemes`** has one row per lexeme, as `ts_stat` gives it: `ndoc` (rows holding it) and `nentry` (its
  occurrences). The row with the empty word, which no lexeme is, holds the table's rows and the sum of their lengths
  (a row's length is its lexemes' occurrences, from `unnest(tsv)`).
- **Kept by triggers.** One plpgsql function, `<table>_lexemes_count`, runs after each INSERT, UPDATE and DELETE
  statement, over its transition tables: it adds the added rows' lexemes and subtracts the removed rows', in word
  order, and deletes words no row holds any more. An upsert fires both the INSERT and the UPDATE trigger. After a
  TRUNCATE it empties the counts.
- **One write at a time per table.** Each write (and a BEFORE statement trigger, for writes from elsewhere) takes a
  transaction-level advisory lock, keyed by the table's oid, before it touches a row, so concurrent writes can't
  deadlock on rows or on counts.
- **Created on first use** by `EnsureLexemesAsync`, under the schema lock, when the table lacks any of the triggers
  (`ananke_lexemes_*`; a table made by an earlier version, or dropped and made again): the counts table, the function
  and the triggers, then the counts filled again from `ts_stat` over the rows already there. Creating
  the triggers takes a lock that keeps writers out until the transaction commits, so no write is missed.
- **The query.** The query's lexemes are looked up in the counts; those no row holds are left out. A lexeme found in
  at most `ceil(RareTermShare × rows)` rows is rare. The rows matched are those holding a rare lexeme (an OR
  tsquery, through the GIN index), or with none rare those holding every lexeme (AND). The tsquery is built from the
  lexemes as they are, quoted, so they aren't read through the configuration a second time. Each matched row is
  scored by BM25 over every query lexeme it holds, with IDF `ln(1 + (N - df + 0.5) / (df + 0.5))`, k1 1.2, b 0.75.
- The counts are table-wide: a filter narrows the rows matched, not the statistics. `InMemoryKnowledgeStore` takes
  its statistics over the rows the filter keeps, and scores every row holding any term.

## Tables

| Table | Columns | Indexes |
|---|---|---|
| store (`ananke_knowledge`) | `id` (key), `text`, `metadata` jsonb, `tsv` (generated from `text`), `embedding` vector(n) | HNSW cosine on `embedding`, GIN on `tsv`, GIN `jsonb_path_ops` on `metadata` |
| catalog (`ananke_catalog`) | `source` (key), `summary`, `keywords` text[], `category`, `indexed_at`, `chunk_count`, `superseded_by`, `embedding` | HNSW cosine on `embedding` |
| store's lexemes (`ananke_knowledge_lexemes`) | `word` (key; `''` for the totals), `ndoc`, `nentry` | — |
| `ananke_embedders` | `table_name` (key), `embedder`, `dimensions` | — |

Table and configuration names are checked against a plain-identifier pattern before they reach SQL; every value is a
parameter.
