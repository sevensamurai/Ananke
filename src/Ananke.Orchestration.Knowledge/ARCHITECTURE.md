# Ananke.Orchestration.Knowledge — Architecture

> Knowledge pipeline — vector stores, document processing, chunking,
> embedding abstractions, knowledge catalog, and document linking.

## Role

Provides the knowledge infrastructure for agents that need to search,
index, and reason over documents. Handles the full pipeline from raw
documents through chunking, embedding, and semantic search.

Separated from `Ananke.Orchestration` so that vector storage backends
(`Ananke.Qdrant`) and document extractors (`Ananke.Documents`) can
depend on knowledge types without pulling in the workflow engine.
`Ananke.Orchestration` then layers bridge/tool types on top of this package.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `IKnowledgeStore` — the vector-indexed store contract — `SearchAsync`, `UpsertAsync`,
   `DeleteAsync` — `src/Ananke.Orchestration.Knowledge/IKnowledgeStore.cs`
2. `IKnowledgeCatalog` — document-level metadata catalog for two-phase discovery (browse,
   then deep-search) — `src/Ananke.Orchestration.Knowledge/Catalog/IKnowledgeCatalog.cs`
3. `DocumentProcessor` — orchestrates the extract → chunk → embed → store pipeline —
   `src/Ananke.Orchestration.Knowledge/Documents/DocumentProcessor.cs`
4. `IDocumentChunker` — splits extracted text into embedding-sized chunks; the pipeline
   stage between extraction and storage — `src/Ananke.Orchestration.Knowledge/Documents/IDocumentChunker.cs`

---

## Dependencies

- `Ananke.Abstractions` (project) — for `IAgentModel`, `IEmbeddingModel`, `AgentMessage`
- `Microsoft.Extensions.DependencyInjection.Abstractions` — for DI extensions
- `Microsoft.Extensions.Logging.Abstractions` — for `ILogger`

**Not** `Ananke.Orchestration` — this project is independent of the workflow engine.

## Namespace → Folder Map

| Namespace | Contents |
|-----------|----------|
| `Ananke.Orchestration.Knowledge` | `IKnowledgeStore`, `InMemoryKnowledgeStore`, `KnowledgeBase`, `KnowledgeSection`, `KnowledgeBaseResult`, `KnowledgeDocument`, `KnowledgeChunk`, `KnowledgeFilter`, `SearchOptions`, `SearchMode`, `SearchResultFormatting`, `ProcessingResult`, `TimeDecay` |
| `Ananke.Orchestration.Knowledge.Catalog` | `IKnowledgeCatalog`, `InMemoryKnowledgeCatalog`, `CatalogAwareKnowledgeStore`, `CatalogKeywordExtractor`, `CatalogEntry`, `CatalogSearchResult`, `CatalogBrowseOptions`, `CatalogEnrichment`, `TimeDecayFunction`, `TimeDecayOptions` |
| `Ananke.Orchestration.Knowledge.Documents` | `IDocumentExtractor`, `IDocumentChunker`, `DocumentProcessor`, `DocumentSummarizer`, `SlidingWindowChunker`, `ExtractedDocument`, `ExtractedSection`, `ExtractedLink`, `ExtractedImage`, `DocumentChunk`, `ChunkingOptions` |
| `Ananke.Orchestration.Knowledge.Embeddings` | `InMemoryEmbedder` |
| `Ananke.Orchestration.Knowledge.Linking` | `DocumentLinkExtractor`, `IDocumentLinkGraph`, `InMemoryDocumentLinkGraph`, `LinkedKnowledgeStore`, `KnowledgeLinkingExtensions`, `KnowledgeLinkingOptions`, `DocumentLink`, `LinkedSearchOptions` |

## Key Types

### Interfaces

| Type | Purpose |
|------|---------|
| `IKnowledgeStore` | Vector-indexed knowledge store — `SearchAsync`, `UpsertAsync`, `DeleteAsync` over embedded document chunks |
| `IKnowledgeCatalog` | Document-level metadata catalog — two-phase discovery (browse sources, then deep-search within) |
| `IDocumentExtractor` | Extracts structured text from file streams (PDF, Markdown, etc.) |
| `IDocumentChunker` | Splits extracted text into embedding-sized chunks with configurable overlap |
| `IDocumentLinkGraph` | Graph of semantic relationships between documents for link-expanded search |

### Implementations

| Type | Purpose |
|------|---------|
| `InMemoryKnowledgeStore` | Cosine-similarity search over in-process vectors — dev/test |
| `InMemoryKnowledgeCatalog` | In-process catalog with keyword + embedding search — dev/test |
| `InMemoryEmbedder` | Deterministic character-hash embedder — testing only (no external deps) |
| `InMemoryDocumentLinkGraph` | In-process document link graph |
| `SlidingWindowChunker` | Splits text with configurable window size and overlap |
| `DocumentProcessor` | Orchestrates extract → chunk → embed → store pipeline |
| `DocumentSummarizer` | Uses an `IAgentModel` to generate knowledge base descriptions |
| `CatalogAwareKnowledgeStore` | Decorator — auto-updates catalog metadata on upsert/delete |
| `CatalogKeywordExtractor` | Uses an `IAgentModel` to extract keywords for catalog enrichment |
| `DocumentLinkExtractor` | Uses an `IAgentModel` to discover semantic links between documents |
| `LinkedKnowledgeStore` | Decorator — expands search results by traversing the document link graph |
| `KnowledgeBase` | Combines a store + catalog entry + description into a named, searchable unit |
| `TimeDecay` | Applies time-based relevance decay to search scores |

### Data Records

| Type | Namespace | Purpose |
|------|-----------|---------|
| `KnowledgeDocument` | Root | Input document for upsert (id + text + metadata) |
| `KnowledgeChunk` | Root | Search result (id + text + score + metadata) |
| `KnowledgeFilter` | Root | Metadata filter for search/delete (key-value pairs) |
| `SearchOptions` | Root | `TopK`, `ScoreThreshold`, `SearchMode`, `Filter`, `SearchResultFormatting` |
| `ProcessingResult` | Root | Pipeline output summary (section count, chunk count) |
| `CatalogEntry` | Catalog | Source metadata: summary, keywords, category, timestamp |
| `CatalogSearchResult` | Catalog | Catalog browse result with relevance score |
| `CatalogEnrichment` | Catalog | LLM-generated metadata for a catalog entry |
| `ExtractedDocument` | Documents | Raw extraction output: sections, links, images |
| `DocumentChunk` | Documents | Individual chunk with text + metadata from chunker |
| `DocumentLink` | Linking | Weighted edge between two documents |
| `LinkedSearchOptions` | Linking | Controls graph traversal depth and score blending |

## Package boundary with `Ananke.Orchestration`

This assembly owns the knowledge pipeline itself. `Ananke.Orchestration` adds bridge types that expose these capabilities through the tool system:

- `KnowledgeSearchTool`
- `KnowledgeTools`
- `KnowledgeCatalogTools`

That split keeps ingestion/search reusable outside workflow execution while still enabling agent-callable knowledge tools in the orchestration package.

## Bridge Types (in `Ananke.Orchestration`)

Three files remain in `Ananke.Orchestration` as bridge code connecting knowledge types
to the `ToolKit` system. These exist because `ToolKit` implementation logic belongs in
the workflow engine, not in the reusable knowledge layer.

| File | Namespace | Purpose |
|------|-----------|---------|
| `src/Ananke.Orchestration/Knowledge/Tools/KnowledgeSearchTool.cs` | `Ananke.Orchestration.Knowledge.Tools` | Registers a knowledge store as a callable agent tool |
| `src/Ananke.Orchestration/Knowledge/Tools/KnowledgeTools.cs` | `Ananke.Orchestration.Knowledge.Tools` | Registers document processing + search as tools (ingest + query) |
| `src/Ananke.Orchestration/Knowledge/Catalog/KnowledgeCatalogTools.cs` | `Ananke.Orchestration.Knowledge.Catalog` | Registers catalog browse/search as agent tools |

## Document Pipeline

```
1. IDocumentExtractor.ExtractAsync(stream)  → ExtractedDocument (sections, links, images)
2. IDocumentChunker.Chunk(extracted)        → DocumentChunk[] (sized for embedding)
3. IKnowledgeStore.UpsertAsync(documents)   → indexed in vector store
4. IKnowledgeStore.SearchAsync(query)       → ranked KnowledgeChunk[]

Embedding happens inside the concrete `IKnowledgeStore` implementation via the configured `IEmbeddingModel`.
```

## Decorator Stack

Knowledge stores compose via the decorator pattern. Each layer is opt-in:

```
IKnowledgeStore (base — InMemoryKnowledgeStore or QdrantKnowledgeStore)
  └─ CatalogAwareKnowledgeStore  (auto-updates IKnowledgeCatalog on upsert/delete)
      └─ LinkedKnowledgeStore    (expands search via IDocumentLinkGraph traversal)
```

Registration order does not matter — decorators are stacked in DI registration order.

## Extension Points

| Interface | Ship with | Add your own for |
|-----------|-----------|-----------------|
| `IKnowledgeStore` | `InMemoryKnowledgeStore` | Pinecone, Weaviate, Azure AI Search, pgvector |
| `IKnowledgeCatalog` | `InMemoryKnowledgeCatalog` | SQL-backed catalog, Elasticsearch |
| `IDocumentExtractor` | — (see `Ananke.Documents`) | DOCX, HTML, Excel, custom formats |
| `IDocumentChunker` | `SlidingWindowChunker` | Semantic chunking, sentence-boundary chunking |
| `IDocumentLinkGraph` | `InMemoryDocumentLinkGraph` | Neo4j, persistent graph storage |
