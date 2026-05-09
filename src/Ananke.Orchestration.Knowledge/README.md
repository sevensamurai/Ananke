# Ananke.Orchestration.Knowledge

[![NuGet](https://img.shields.io/nuget/v/Ananke.Orchestration.Knowledge.svg)](https://www.nuget.org/packages/Ananke.Orchestration.Knowledge)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

Knowledge pipeline for Ananke — vector knowledge stores, document processing, chunking, embedding abstractions, knowledge catalog with LLM-enriched metadata, and cross-document linking.

## Install

```bash
dotnet add package Ananke.Orchestration.Knowledge
```

> **Note:** If you use `Ananke.Orchestration`, this package is already included as a transitive dependency.

## Quick start

### Semantic search

```csharp
using Ananke.Orchestration.Knowledge;

// InMemoryKnowledgeStore for dev/test; QdrantKnowledgeStore for production
var store = new InMemoryKnowledgeStore(embeddingModel);

await store.UpsertAsync([
    new KnowledgeDocument { Id = "doc-1", Text = "Ananke is a .NET agent orchestration library." },
    new KnowledgeDocument { Id = "doc-2", Text = "Workflows are built with a fluent graph-as-code API." },
]);

var results = await store.SearchAsync("how do I build a workflow?");
// results[0].Text => "Workflows are built with a fluent graph-as-code API."
```

### Document ingestion pipeline

```csharp
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Documents;

var processor = new DocumentProcessor(
    new HttpClient(),
    [new PdfExtractor(), new MarkdownExtractor()],  // from Ananke.Documents
    new SlidingWindowChunker(),
    knowledgeStore);

await using var stream = File.OpenRead("architecture.pdf");
var result = await processor.ProcessAsync(stream, "application/pdf", sourceId: "arch-doc");
// result.ChunkCount => 42
```

### Knowledge catalog (two-phase discovery)

```csharp
using Ananke.Orchestration.Knowledge.Catalog;

var catalog = new InMemoryKnowledgeCatalog(embeddingModel);

// Wrap any store to auto-update catalog on upsert/delete
var catalogStore = new CatalogAwareKnowledgeStore(innerStore, catalog, enricher);

// Phase 1: discover relevant sources
var sources = await catalog.BrowseAsync(new CatalogBrowseOptions { Query = "deployment" });

// Phase 2: deep-search within matched sources
var chunks = await catalogStore.SearchAsync("kubernetes deployment", new SearchOptions
{
    Filter = new KnowledgeFilter { ["source_id"] = sources[0].Entry.SourceId }
});
```

### Cross-document linking

```csharp
using Ananke.Orchestration.Knowledge.Linking;

// Opt-in via DI — decorates IKnowledgeStore with graph-expanded search
services.AddKnowledgeLinking(options =>
{
    options.AutoLinkOnIngest = true;       // LLM-based post-ingestion linking
    options.SimilarityThreshold = 0.7f;    // link documents above this score
});
```

## Key types

| Type | Kind | Purpose |
|------|------|---------|
| `IKnowledgeStore` | Interface | Vector-indexed store — `SearchAsync`, `UpsertAsync`, `DeleteAsync` |
| `InMemoryKnowledgeStore` | Class | In-process implementation for dev/test |
| `IKnowledgeCatalog` | Interface | Document-level metadata catalog for two-phase discovery |
| `InMemoryKnowledgeCatalog` | Class | In-process catalog implementation |
| `CatalogAwareKnowledgeStore` | Class | Decorator — auto-updates catalog on store operations |
| `IDocumentExtractor` | Interface | Extracts structured text from files (PDF, Markdown, etc.) |
| `IDocumentChunker` | Interface | Splits text into embedding-sized chunks |
| `SlidingWindowChunker` | Class | Sliding window chunker with configurable overlap |
| `DocumentProcessor` | Class | Orchestrates the full extract → chunk → embed → store pipeline |
| `DocumentSummarizer` | Class | Uses an LLM to generate knowledge base descriptions |
| `DocumentLinkExtractor` | Class | Discovers semantic links between documents via LLM |
| `LinkedKnowledgeStore` | Class | Decorator — expands search results through document link graph |
| `KnowledgeBase` | Class | Combines a store, catalog entry, and description into a named unit |
| `InMemoryEmbedder` | Class | Deterministic character-hash embedder for testing |

## The pipeline

```
1. IDocumentExtractor.ExtractAsync(stream) → structured text + sections
2. IDocumentChunker.Chunk(text)            → DocumentChunk[]
3. IEmbeddingModel.EmbedBatchAsync(chunks) → float[][] vectors
4. IKnowledgeStore.UpsertAsync(documents)  → indexed in vector store
5. IKnowledgeStore.SearchAsync(query)      → ranked KnowledgeChunk[]
```

Optional enrichment layers compose via decoration:

```
IKnowledgeStore
  └─ CatalogAwareKnowledgeStore  (auto-catalogs on upsert)
      └─ LinkedKnowledgeStore    (graph-expanded search)
```

## Implementations

| Interface | In-memory (this package) | Distributed |
|-----------|------------------------|-------------|
| `IKnowledgeStore` | `InMemoryKnowledgeStore` | `QdrantKnowledgeStore` (`Ananke.Qdrant`) |
| `IKnowledgeCatalog` | `InMemoryKnowledgeCatalog` | `QdrantKnowledgeCatalog` (`Ananke.Qdrant`) |
| `IDocumentLinkGraph` | `InMemoryDocumentLinkGraph` | — |
| `IDocumentExtractor` | — | `PdfExtractor`, `MarkdownExtractor` (`Ananke.Documents`) |
| `IEmbeddingModel` | `InMemoryEmbedder` | `OpenAIEmbeddingModel`, `GeminiEmbeddingModel` |

## Dependencies

- `Ananke.Abstractions` — for `IAgentModel`, `IEmbeddingModel`, `AgentMessage`

This package has **no dependency on `Ananke.Orchestration`** — it is independent of the workflow engine.

## Related packages

| Package | What it adds |
|---------|-------------|
| `Ananke.Orchestration` | ToolKit bridge — registers knowledge search as agent-callable tools |
| `Ananke.Documents` | `PdfExtractor` and `MarkdownExtractor` for `DocumentProcessor` |
| `Ananke.Qdrant` | Distributed vector store implementations via Qdrant |
| `Ananke.Orchestration.OpenAI` | `OpenAIEmbeddingModel` for production embeddings |
| `Ananke.Orchestration.Google` | `GeminiEmbeddingModel` for production embeddings |

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
