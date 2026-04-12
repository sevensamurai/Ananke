# LongTermMemoryDemo

End-to-end knowledge pipeline: batch document import, agent Q&A with autonomous search, and conversational knowledge building where the agent indexes and queries documents in the same chat session.

## What it demonstrates

| Capability | How |
|---|---|
| **Batch document import** | `DocumentProcessor` extracts a local PDF, chunks it, embeds it, and stores it in the knowledge store |
| **Auto-description** | LLM summarizes what the document covers — used as the search tool description |
| **Knowledge catalog** | `CatalogAwareKnowledgeStore` auto-maintains document-level metadata with keywords, categories, and time-decay reranking |
| **Cross-document linking** | `LinkedKnowledgeStore` + `DocumentLinkExtractor` discover relationships between documents and expand search results through a link graph |
| **Agent-autonomous search** | Agent decides when to call `search_engineering_docs` based on the question — no explicit instruction needed |
| **Conversational knowledge building** | `KnowledgeTools` gives the agent both `process_document` and `search_knowledge` — user says "index this URL", agent does it, and answers from it immediately |
| **In-memory / Qdrant backends** | Same code, swap the store — `InMemoryKnowledgeStore` for dev, `QdrantKnowledgeStore` for production |

## Pipeline flow

```
Step 1: Batch import
  refactoring.pdf → PdfExtractor → SlidingWindowChunker → EmbeddingModel → KnowledgeStore

Step 2: Agent Q&A (search-only)
  User question → Agent → search_knowledge → KnowledgeStore → grounded answer

Step 3: Conversational knowledge building
  "Index this URL" → Agent → process_external_url → DocumentProcessor → KnowledgeStore
                   → Agent → search_knowledge → KnowledgeStore → grounded answer

Step 4: Cross-document linking (--linking)
  New document ingested → DocumentLinkExtractor → LLM classifies relationships
    → IDocumentLinkGraph stores links (references, extends, prerequisite, ...)
    → search_knowledge expands results through graph traversal

Step 5: Cross-document query
  "How do refactoring principles relate to .NET contribution guidelines?"
    → search_knowledge → vector results + graph-expanded results → merged answer
```

## Running locally

### 1. Configure secrets

Edit `secrets.json` with your OpenAI API key:

```json
{
  "OpenAI": {
    "ApiKey": "sk-proj-your-key-here",
    "Model": "gpt-4.1-mini"
  }
}
```

### 2. Run

```bash
cd src

# In-memory store (no infrastructure needed)
dotnet run --project demos/LongTermMemoryDemo

# Qdrant-backed store
docker compose -f demos/LongTermMemoryDemo/docker-compose.yml up -d
dotnet run --project demos/LongTermMemoryDemo -- --qdrant

# With knowledge catalog (LLM-enriched metadata + time decay)
dotnet run --project demos/LongTermMemoryDemo -- --catalog

# With cross-document linking (graph-expanded search)
dotnet run --project demos/LongTermMemoryDemo -- --linking

# Catalog + linking (full pipeline)
dotnet run --project demos/LongTermMemoryDemo -- --catalog --linking

# Full featured: Qdrant + catalog + linking
dotnet run --project demos/LongTermMemoryDemo -- --qdrant --catalog --linking
```

### Flags

| Flag | What it adds |
|---|---|
| `--qdrant` | Uses Qdrant vector database instead of in-memory store (requires `docker compose up`) |
| `--catalog` | Enables `CatalogAwareKnowledgeStore` — LLM-enriched keywords, categories, summaries, and time-decay reranking |
| `--linking` | Enables `LinkedKnowledgeStore` + `DocumentLinkExtractor` — cross-document link discovery and graph-expanded search |

## Cross-document linking (`--linking`)

When `--linking` is enabled, the demo wraps the knowledge store with `LinkedKnowledgeStore` and registers a `DocumentLinkExtractor`. After each document is ingested, the extractor:

1. **Finds candidates** — searches the store for chunks semantically similar to each new chunk
2. **Classifies relationships** — asks the LLM to classify each pair as `references`, `extends`, `prerequisite`, `example-of`, `contradicts`, or `none`
3. **Stores links** — writes directed, weighted edges into an `IDocumentLinkGraph`

At search time, `LinkedKnowledgeStore` performs **dual retrieval**:

```
Query → Vector similarity search     → ranked results (standard)
Query → Graph traversal from top hits → linked results (expansion)
        ──────────────────────────────────────────────
        Merged, deduplicated, re-ranked by combined score
```

This means a query about "refactoring and code quality" can surface chunks from the .NET CONTRIBUTING.md even if they use different terminology, because the link extractor established the relationship during ingestion.

The decorator stacking order is:

```csharp
InMemoryKnowledgeStore          // vector storage
  → CatalogAwareKnowledgeStore  // catalog metadata + time decay (--catalog)
    → LinkedKnowledgeStore      // graph-expanded search (--linking)
```

## Security note

`KnowledgeTools` includes `process_document`, which lets the agent fetch and index arbitrary URLs. **In production, grant `KnowledgeTools` only to trusted roles** (admins, knowledge curators). For untrusted users, use `KnowledgeSearchTool` instead — it provides search-only access with no ingestion capability.

```csharp
// ✅ Trusted users: can index AND search
var adminTools = KnowledgeTools.Create(processor, store);

// ✅ Untrusted users: search only, no ingestion
var userTools = KnowledgeSearchTool.Create("knowledge", store);
```

This is a standard authorization concern — the same way you wouldn't give every user write access to a database, you wouldn't give every user the ability to index documents into the knowledge base.

## Project structure

```
LongTermMemoryDemo/
├── Program.cs             — All demo steps: batch import, Q&A, conversational building
├── refactoring.pdf        — Sample PDF for batch import (step 1)
├── secrets.json           — API keys (gitignored)
├── docker-compose.yml     — Qdrant container for persistent storage
└── README.md              — This file
```
