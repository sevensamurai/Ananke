# Ananke.Qdrant

[![NuGet](https://img.shields.io/nuget/v/Ananke.Qdrant.svg)](https://www.nuget.org/packages/Ananke.Qdrant)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

Qdrant vector database provider for Ananke — `IKnowledgeStore` and `IKnowledgeCatalog` implementations with dense vector search, metadata filtering, automatic collection management, and time-decay–aware catalog.

## Install

```bash
dotnet add package Ananke.Qdrant
```

Requires a running Qdrant instance. Quickest way:

```bash
docker run -p 6333:6333 -p 6334:6334 qdrant/qdrant
```

## Quick start

### Knowledge store (chunk-level vector search)

```csharp
using Ananke.Orchestration.Knowledge;
using Ananke.Qdrant;
using Qdrant.Client;

var client = new QdrantClient("localhost", 6334);
var embedder = OpenAIEmbeddingModel.Create(apiKey);

// Creates the "knowledge" collection automatically on first use
var store = new QdrantKnowledgeStore(client, embedder);

// Ingest documents via the standard pipeline
var processor = new DocumentProcessor(httpClient, extractors, chunker, store);
await processor.ProcessAsync(pdfStream, "application/pdf", "design-patterns.pdf");

// Semantic search
var results = await store.SearchAsync("factory method pattern", new SearchOptions { TopK = 5 });
```

### Knowledge catalog (document-level discovery + time decay)

The catalog maintains one entry per source document in a separate Qdrant collection (`knowledge_catalog`). Each entry is enriched with LLM-extracted keywords, a category, and a summary — enabling agents to *discover what's in the knowledge base* before deep-searching individual chunks.

```csharp
var catalog = new QdrantKnowledgeCatalog(client, embedder);

// Optional: LLM-based keyword/category/summary extraction on ingest
var extractor = new CatalogKeywordExtractor(agentModel);

// Wrap any IKnowledgeStore — catalog updates happen automatically on upsert/delete
var catalogStore = new CatalogAwareKnowledgeStore(
    store, catalog, extractor,
    new TimeDecayOptions { HalfLifeDays = 90, FloorWeight = 0.3f });

// Use catalogStore as a drop-in IKnowledgeStore replacement.
// Upserts now auto-maintain the catalog with timestamps + LLM-enriched metadata.
// Searches now apply time-decay reranking (newer documents score higher).
```

**Time-decay behavior:** a chunk's final score = `vectorSimilarity × decayWeight(age)`. With a 90-day half-life and 0.3 floor: a document indexed today scores at 100 %, 90 days ago at 50 %, and anything older than ~270 days bottoms out at 30 %. Between equally relevant documents on the same topic, the fresher one always wins.

### Agent tools

Give agents both chunk-level search and document-level discovery:

```csharp
// Chunk-level search (existing)
var searchTools = KnowledgeSearchTool.Create("knowledge", catalogStore,
    description: "Search indexed engineering documents.");

// Document-level catalog browsing and discovery (new)
var catalogTools = KnowledgeCatalogTools.Create(catalog);

// Merge into a single toolkit — agent gets search + browse + discover
searchTools.Merge(catalogTools);
```

The agent now has three tools:

| Tool | What it does |
|---|---|
| `search_engineering_docs` | Semantic search over individual text chunks |
| `browse_catalog` | List all indexed documents with keywords, categories, and timestamps |
| `discover_sources` | Semantic search over document summaries to find relevant sources |

This enables **two-phase discovery**: the agent first discovers which documents are relevant via the catalog, then deep-searches within those sources for specific information.

## What it registers

| Service | Qdrant collection | Description |
|---|---|---|
| `QdrantKnowledgeStore` | `knowledge` (configurable) | Chunk-level embeddings for semantic search |
| `QdrantKnowledgeCatalog` | `knowledge_catalog` (configurable) | Document-level summaries for catalog discovery |

Both collections are created automatically on first use with cosine distance.

## Configuration

```csharp
// Custom collection names and vector dimensions
var store = new QdrantKnowledgeStore(
    client, embedder,
    collectionName: "my_docs",        // default: "knowledge"
    vectorSize: 3072);                 // default: 1536 (text-embedding-3-small)

var catalog = new QdrantKnowledgeCatalog(
    client, embedder,
    collectionName: "my_docs_catalog", // default: "knowledge_catalog"
    vectorSize: 3072);
```

## Demo

The [`LongTermMemoryDemo`](../demos/LongTermMemoryDemo/) shows the full pipeline end-to-end:

```bash
# In-memory store (no infrastructure needed)
dotnet run

# Qdrant-backed store
docker compose up -d && dotnet run -- --qdrant

# Qdrant + knowledge catalog with time decay
docker compose up -d && dotnet run -- --qdrant --catalog
```

## Related packages

| Package | What it adds |
|---|---|
| `Ananke.Orchestration` | Core: workflows, agents, knowledge pipeline, in-memory store |
| `Ananke.Orchestration.OpenAI` | OpenAI chat models + embedding models |
| `Ananke.Documents` | Document extractors (PDF, Markdown) for the knowledge pipeline |
| `Ananke` | Meta-package — includes everything |

## Documentation

Full docs, demos, and architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)