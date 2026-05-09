# Architecture: Knowledge Pipeline

> Part of the [Architecture Guide](../ARCHITECTURE.md). Covers vector stores, document processing, catalog, and document linking.

---

## Overview

The knowledge subsystem lives in `Ananke.Orchestration.Knowledge` (core abstractions) and `Ananke.Documents` (extractors). It provides a full RAG pipeline from raw documents to semantic search.

```mermaid
flowchart TD
    subgraph Ingestion
        DOC[Raw Document] --> EXT[IDocumentExtractor<br/>PDF · Markdown · Text]
        EXT --> CHUNK[IDocumentChunker<br/>SlidingWindowChunker]
        CHUNK --> PROC[DocumentProcessor<br/>orchestrates pipeline]
    end

    subgraph Storage
        PROC --> EMB[IEmbeddingModel]
        EMB --> KS[IKnowledgeStore<br/>vector-indexed chunks]
        KS --> CAT[IKnowledgeCatalog<br/>document metadata + keywords]
        KS --> LINK[IDocumentLinkGraph<br/>cross-document references]
    end

    subgraph Retrieval
        Q[Query] --> SEARCH[KnowledgeBase.SearchAsync]
        SEARCH --> KS
        SEARCH --> CAT
        SEARCH --> RANK[Time-decay reranking]
        RANK --> RESULTS[Ranked chunks]
    end
```

## Core Types

### Document Processing

| Type | Assembly | Purpose |
|---|---|---|
| `IDocumentExtractor` | Knowledge | Extract raw text from a document format |
| `IDocumentChunker` | Knowledge | Split text into overlapping chunks |
| `SlidingWindowChunker` | Knowledge | Default chunker with configurable window/overlap |
| `DocumentProcessor` | Knowledge | Orchestrates extract → chunk → embed → store |
| `DocumentSummarizer` | Knowledge | LLM-powered document summaries for catalog |

### Storage

| Type | Assembly | Purpose |
|---|---|---|
| `IKnowledgeStore` | Knowledge | Store and search vector-indexed chunks |
| `InMemoryKnowledgeStore` | Knowledge | In-memory impl (cosine similarity) |
| `IKnowledgeCatalog` | Knowledge | Document-level metadata, keywords, summaries |
| `InMemoryKnowledgeCatalog` | Knowledge | In-memory impl |
| `CatalogAwareKnowledgeStore` | Knowledge | Decorator that auto-updates catalog on ingest |
| `CatalogKeywordExtractor` | Knowledge | Extract keywords from chunks for catalog |

### Document Linking

| Type | Purpose |
|---|---|
| `IDocumentLinkGraph` | Track cross-document references |
| `InMemoryDocumentLinkGraph` | In-memory graph |
| `DocumentLinkExtractor` | Discover links between documents |
| `LinkedKnowledgeStore` | Decorator that follows links during search |

### External Providers

| Package | Implements | Backend |
|---|---|---|
| `Ananke.Qdrant` | `IKnowledgeStore`, `IKnowledgeCatalog`, `IEmpiricalMemory` | Qdrant vector DB |

## `KnowledgeBase` Facade

`KnowledgeBase` is the high-level API that composes the pipeline:
- `IngestAsync(document)` — full pipeline from raw doc to indexed chunks
- `SearchAsync(query, topK)` — semantic search with time-decay reranking
- Integrates with `KnowledgeSearchTool` for agent tool calling

## Time-Decay Reranking

`TimeDecay` applies a configurable decay function to search scores based on document age. Newer knowledge ranks higher, preventing stale information from dominating results.

## Agent Integration

`KnowledgeSearchTool` and `KnowledgeCatalogTools` are pre-built `ToolDefinition` entries that agents can use to search the knowledge base during conversations.
