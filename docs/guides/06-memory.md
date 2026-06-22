<!-- topic: long-term-memory, tags: memory, knowledge, vector, embedding, search, ingestion, knowledge-base, catalog -->
# 06 — Long-Term Memory

Build a knowledge pipeline that extracts, chunks, embeds, and stores documents
for semantic search — with agent-driven ingestion, catalog discovery, and
time-decay reranking.

**Demo:** [LongTermMemoryDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/03-memory-and-knowledge/LongTermMemoryDemo)

---

## The Knowledge Pipeline

Ananke's memory system follows a composable pipeline:

```
Document → Extract → Chunk → Embed → Store → Search
```

| Component | Interface | What it does |
|---|---|---|
| Extractor | `IDocumentExtractor` | Converts raw formats (PDF, Markdown) to normalized Markdown |
| Chunker | `IDocumentChunker` | Splits documents at heading boundaries with configurable overlap |
| Embedding | `IEmbeddingModel` | Converts text chunks into vector representations |
| Store | `IKnowledgeStore` | Vector-indexed storage with semantic search |
| Processor | `DocumentProcessor` | Orchestrates the full pipeline |

---

## Quick Start — Programmatic Ingestion

```csharp
using Ananke.Documents;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.OpenAI;

// 1. Set up the pipeline components
var embeddingModel = OpenAIEmbeddingModel.Create(apiKey);
var knowledgeStore = new InMemoryKnowledgeStore(embeddingModel);
var processor = new DocumentProcessor(
    new HttpClient(),
    [new PdfExtractor(), new MarkdownExtractor()],
    new SlidingWindowChunker(),
    knowledgeStore);

// 2. Ingest a PDF
await using var pdf = File.OpenRead("onboarding-policy.pdf");
var result = await processor.ProcessAsync(pdf, ".pdf", "onboarding-policy");
Console.WriteLine($"{result.Sections} sections, {result.Chunks} chunks stored");

// 3. Search
var hits = await knowledgeStore.SearchAsync("onboarding process for new engineers");
foreach (var hit in hits)
    Console.WriteLine($"  [{hit.Score:F2}] {hit.Text[..80]}...");
```

---

## Agent-Driven Ingestion

Give an agent tools to index documents and search them in the same conversation:

```csharp
using Ananke.Orchestration.Agents;

// KnowledgeTools provides both process_document and search_knowledge
var tools = KnowledgeTools.Create(processor, knowledgeStore,
    searchDescription: "Search indexed engineering reference materials.",
    describeModel: model);  // auto-generate LLM summaries on ingest

await StreamingChatWorkflow.Create("chat", model)
    .WithSystemPrompt("You can index documents and search them for the user.")
    .WithTools(tools)
    .OnTextDelta(async delta => Console.Write(delta))
    .Build()
    .RunAsync(new StreamingChatState
    {
        Messages = [AgentMessage.User(
            "Index https://example.com/design-patterns.pdf and tell me about the factory pattern")]
    });
```

The user says *"index this PDF"*, the agent processes it, and it's immediately
searchable — no admin panel, no batch job.

---

## Search-Only Tool

If documents are pre-indexed, give the agent a search-only tool:

```csharp
var tools = KnowledgeSearchTool.Create("knowledge", knowledgeStore,
    description: "Search the knowledge base for information from indexed documents.");

await StreamingChatWorkflow.Create("chat", model)
    .WithSystemPrompt("Use search_knowledge to find information from indexed documents.")
    .WithTools(tools)
    .OnTextDelta(async delta => Console.Write(delta))
    .Build()
    .RunAsync(new StreamingChatState
    {
        Messages = [AgentMessage.User("What's the onboarding process for new engineers?")]
    });
```

---

## Document Extractors

### PDF Extractor

Extracts text from PDFs preserving headings, links, and structure as Markdown:

```csharp
using Ananke.Documents;

var extractor = new PdfExtractor();
// Used automatically by DocumentProcessor when content type is ".pdf" or "application/pdf"
```

### Markdown Extractor

Parses Markdown structure into normalized sections:

```csharp
var extractor = new MarkdownExtractor();
// Used for ".md" content types
```

### Plain Text Extractor

```csharp
var extractor = new PlainTextExtractor();
// Used for ".txt" content types
```

---

## Auto-Description

Generate an LLM summary of what the document covers:

```csharp
var result = await processor.ProcessAsync(stream, ".pdf", "design-patterns.pdf");

// Ask the LLM to summarize the document
result = await result.AutoDescribeAsync(model, knowledgeStore);
Console.WriteLine($"Description: {result.Description}");
```

---

## Knowledge Catalog

Wrap any knowledge store with a catalog layer for document-level discovery.
The catalog maintains metadata (keywords, categories, timestamps) automatically
as documents are ingested.

```csharp
// Create a catalog
var catalog = new InMemoryKnowledgeCatalog(embeddingModel);
// Or: new QdrantKnowledgeCatalog(qdrantClient, embeddingModel);

// LLM extracts keywords, category, and summary on ingest
var extractor = new CatalogKeywordExtractor(chatModel);

// Wrap the store — upserts now auto-maintain the catalog
var catalogStore = new CatalogAwareKnowledgeStore(
    knowledgeStore, catalog, extractor,
    new TimeDecayOptions { HalfLifeDays = 90, FloorWeight = 0.3f });
```

### Time-Decay Reranking

Fresher documents score higher. Configure the decay curve:

| Option | Default | Description |
|---|---|---|
| `HalfLifeDays` | 90 | After this many days, a document's weight drops to 50% |
| `FloorWeight` | 0.3 | Minimum weight — old documents never drop below this |

### Catalog Discovery Tools

Give agents tools to browse sources before deep-searching:

```csharp
var tools = KnowledgeSearchTool.Create("knowledge", catalogStore)
    .Merge(KnowledgeCatalogTools.Create(catalog));
```

### Browsing the Catalog

```csharp
var entries = await catalog.BrowseAsync();
foreach (var entry in entries)
{
    Console.WriteLine($"  Source:   {entry.Source}");
    Console.WriteLine($"  Category: {entry.Category}");
    Console.WriteLine($"  Keywords: {string.Join(", ", entry.Keywords)}");
    Console.WriteLine($"  Summary:  {entry.Summary}");
    Console.WriteLine($"  Chunks:   {entry.ChunkCount}");
}
```

---

## Persistent Store — Qdrant

For production, use Qdrant for persistent, distributed vector storage:

```bash
dotnet add package Ananke.Qdrant
```

```csharp
using Ananke.Qdrant;
using Qdrant.Client;

IKnowledgeStore store = new QdrantKnowledgeStore(
    new QdrantClient("localhost", 6334), embeddingModel);

IKnowledgeCatalog catalog = new QdrantKnowledgeCatalog(
    new QdrantClient("localhost", 6334), embeddingModel);
```

---

## What's Next

| Next guide | What you'll learn |
|---|---|
| [07 — Human-in-the-Loop](07-human-in-the-loop.md) | Pause workflows for human approval |
| [08 — State Machine](08-state-machine.md) | Production FSM for long-running services |

**Also see:** [EntityMemoryDemo](https://github.com/sevensamurai/Ananke/tree/main/src/demos/03-memory-and-knowledge/EntityMemoryDemo) — per-entity memory isolation using `EntityMemoryProvider`; the same workflow handles multiple customers with fully isolated empirical and knowledge stores.

---

← [Back to Learning Path](../learning-path.md)
