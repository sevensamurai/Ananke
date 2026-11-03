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

### Questions That Span Documents

A question that compares two documents needs a passage from each, but one long document can fill every result. Limit
how many results come from one document (`source`), and join a document's best passages into one result so the top
results count documents:

```csharp
var tools = KnowledgeSearchTool.Create("knowledge", knowledgeStore, defaultOptions: new SearchOptions
{
    Mode = SearchMode.Sparse,
    TopK = 6,
    MaxPerSource = 3,       // up to three passages from one document…
    JoinPerSource = true,   // …joined into one result, so TopK counts documents
});
```

A store ranks more candidates to fill `TopK` (`SearchResultLimits.Candidates`). Without `JoinPerSource`, each passage
is a result of its own and `MaxPerSource` only caps them.

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

### Reading a Document's Header

Most documents say what they are near the top: a title, a status, a date. Give `DocumentProcessor` a header reader
(experimental: `ANNKEEXP004`), name the fields to keep, and each chunk starts with them and carries them in its
metadata, so a passage says where it is from and how current it is:

```csharp
#pragma warning disable ANNKEEXP004
var processor = new DocumentProcessor(http, extractors, chunker, store)
{
    Headers = new DocumentHeaderOptions
    {
        Read = DocumentHeaders.Combine(DocumentHeaders.FromFrontMatter, DocumentHeaders.FromStatusSection),
        Fields = ["Title", "Status", "Date"],
        DateField = "Date",
    },
};
// Each chunk's text starts "[Title: … · Status: … · Date: 2026-11-02]".
```

Readers for the common shapes, each from the extracted document to fields compared without case:

| Reader | Reads | As written in |
|---|---|---|
| `FromFrontMatter` | YAML front matter (which `MarkdownExtractor` reads into the document's metadata) | Jekyll, Hugo, Docusaurus, Obsidian, MADR |
| `FromFieldBlock` | `Key: value` lines at the very top, indented lines continuing | PEPs, mail-style headers |
| `FromStatusSection` | the first heading, a `## Status` section's first line, a `Date:` line | Nygard-style decision records |
| `FromMarkdown` | the first heading, and `**Key:** value` or `**Key**: value` in the first section, or the second when the first has none | many hand-written notes |

`Combine` tries several, taking each field from the first that has it; `Read` can be any function of your own, for a
layout of its own.

- **Only the fields named are kept.** Every field read counts for the date and superseding, but only those in
  `Fields` go on chunks, into metadata, or to the records step. None by default: headers are often where people are
  named.
- **Metadata kept beside a document** — a `kep.yaml`, a database row — goes in as `ProcessAsync`'s `tags`: a tag
  fills a field the document doesn't give. Tags are also written to every chunk's metadata, whatever `Fields` names,
  so pass only what may be stored: the superseding lists of a `kep.yaml`, not its authors.
- **The date field's first date** is written as `effective_at`: `2026-11-02`, `05-Jul-2001`, `July 5, 2001`, or
  `June 2022` (its first day). `ParseDate` replaces the parser. A `CatalogAwareKnowledgeStore` keeps it as the entry's
  `EffectiveAt`, and time decay ages the document from it.
- **`DropSections`** leaves sections out by heading (an *Authors' Addresses*, an attendee list, a template's
  checklist), and **`CleanText`** cleans each section's text (page footers) before chunking and records. The header is
  read before either.

### Dated Records

Passages answer "what does this say". A knowledge base about decisions also needs "what was decided, when, and what
did it replace" — which a passage may hold without search finding it. `DatedRecordExtractor<TRecord>`
(experimental: `ANNKEEXP004`) reads typed records from each document with one model call held to your record type's
JSON schema, and keeps them in a store of their own, replaced whenever their document is indexed again:

```csharp
#pragma warning disable ANNKEEXP004
public enum DecisionStatus { Decided, Proposed, Parked, Rejected, Replaced }
public sealed record Decision(string Statement, DecisionStatus Status, string? Date, string? Replaces);

var records = new InMemoryKnowledgeStore(embeddingModel);   // or a second Postgres table
var processor = new DocumentProcessor(http, extractors, chunker, store)
{
    Headers = new DocumentHeaderOptions { Read = DocumentHeaders.FromMarkdown, DateField = "Date" },
    Records = new DatedRecordExtractor<Decision>(
        extractionModel.WithSpendingLimit(new SpendingLimit(maxCost: 1m, maxCappedReplies: 3)),
        records,
        new DatedRecordOptions<Decision>
        {
            Instructions = "Extract the decisions this document records, one sentence each. Name no people.",
            Text = d => d.Statement,
            Date = d => d.Date,
            Replaces = d => d.Replaces,
        }),
};
```

Each record carries its source, its date (its own, else the document's, as `effective_at`), what it replaces, the
header's fields, and itself as JSON — `DatedRecordExtractor<Decision>.Read(chunk)` returns the typed record. A
`Keep` function applies a rule of yours before anything is stored. A reply that isn't the schema's JSON throws
`RecordExtractionException`, and the source is left with no records rather than stale ones.

**What is current.** `DatedRecordReader<TRecord>` searches the records and returns them newest first, each with
`ReplacedBy` set when it was replaced — explicitly only, never by similarity:

- its document's header says the document is superseded (`DocumentHeaderOptions.SupersededBy`, a function from the
  header's fields to what supersedes it; the catalog entry's `SupersededBy` is set from it too, so discovery leaves the
  document out), or
- a newer record returned with it names it in its `replaces`: every word of three or more letters there appears in
  the older record.

```csharp
Headers = new DocumentHeaderOptions
{
    Read = DocumentHeaders.FromMarkdown,
    DateField = "Date",
    SupersededBy = f => f.TryGetValue("Status", out var s) && s.StartsWith("Superseded") ? [s] : null,
},
// …
var decisions = await new DatedRecordReader<Decision>(records).SearchAsync("telemetry backend");
var current = decisions.Where(d => d.ReplacedBy is null);
```

**Lists, not searches.** A search returns the closest few, so a roundup ("every decision parked this quarter") can
miss some. A store that implements `IListableKnowledgeStore` — the in-memory, Postgres and Qdrant stores do, and the
scoped and catalog-aware stores pass it through — lists every chunk whose metadata matches a filter. Name the record
properties to keep as metadata with `DatedRecordOptions.Fields`, then list by them:

```csharp
Fields = d => new Dictionary<string, string> { ["status"] = d.Status.ToString().ToLowerInvariant() },
// …
var parked = await reader.ListAsync(new KnowledgeFilter { ["status"] = "parked" }, since: quarterStart);
```

`DatedRecordTools.Create(reader, ["status"])` gives agents the same as a `list_records` tool, with an optional
parameter for each property named and `since`. Both are experimental: `ANNKEEXP004`.

### Links by Pattern

Some links need no model: a ticket key, a document's file name, a standard's number. Give
`DocumentProcessor.LinkPatterns` (and `DatedRecordOptions.LinkPatterns`, for records) patterns by the kind of thing
each finds, experimental: `ANNKEEXP004`. Each match in a chunk's or record's text is written into its metadata as
`entity:{kind}:{match}` = `{kind}:{match}`:

```csharp
#pragma warning disable ANNKEEXP004
var processor = new DocumentProcessor(http, extractors, chunker, store)
{
    LinkPatterns = new Dictionary<string, Regex>
    {
        ["card"] = new(@"\bZEN-\d+\b"),            // anchored to your own keys
        ["rfc"] = new(@"\bRFC ?(?<key>\d{3,4})\b"),  // a "key" group links just the number
    },
};

// Everything that mentions ZEN-6:
var mentions = await store.SearchAsync("ZEN-6", new SearchOptions
{
    Mode = SearchMode.Sparse,
    Filter = new KnowledgeFilter { ["entity:card:ZEN-6"] = "card:ZEN-6" },
});
```

Anchor a pattern to what it means in your documents: a loose `[A-Z]+-\d+` finds `UTF-8` and `SHA-256` as readily as
a ticket key. `DocumentStructureBuilder` (in `Ananke.Learning`)
turns the same metadata into `mentions` edges on an `IKnowledgeGraph`, marked `EdgeProvenance.Extracted`.

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

Fresher documents score higher. A document's age is counted from its own date (`CatalogEntry.EffectiveAt`, read
from its header) when it has one, and from when it was indexed otherwise, so re-indexing an old document doesn't
make it new. Configure the decay curve:

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

`list_documents` lists every document whose header field has a value — every plan, with its status — newest first by
the document's own date. It reads the header fields ingestion kept (`DocumentHeaderOptions.Fields`), which a
`CatalogAwareKnowledgeStore` copies into each entry's `Fields`.

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

// Every plan, newest first by its own date:
var plans = await catalog.BrowseAsync(new CatalogBrowseOptions
{
    Field = "Type",
    Value = "plan",
    Order = CatalogBrowseOrder.EffectiveAt,
});
```

---

## Local embedding models

`OpenAIEmbeddingModel` takes an `endpoint`, so an Ollama or vLLM server serves embeddings the same way
it serves chat:

```csharp
var embeddingModel = OpenAIEmbeddingModel.Create(
    apiKey: "no-key-required",
    model: "nomic-embed-text",
    endpoint: new Uri("http://model-host:11434/v1"));
```

For a knowledge base this is often a stronger privacy argument than it is for chat — the documents
you ingest are the private material, and they never leave the host.

With no model server at all, `OnnxEmbeddingModel` (package `Ananke.Embeddings.Onnx`, experimental:
`ANNKEEXP004`) runs a BERT-style ONNX model in the process. Point it at a directory with the model and its
vocabulary; nothing is downloaded:

```csharp
#pragma warning disable ANNKEEXP004
using var embeddingModel = new OnnxEmbeddingModel(new OnnxEmbeddingOptions
{
    ModelDirectory = "/models/bge-small-en-v1.5",
    ModelFile = "model_optimized.onnx",
    Pooling = OnnxPooling.Cls,   // Mean for sentence-transformers models
});
```

Two consequences to plan for before you ingest anything:

- **Vector size must match the model.** Local embedders commonly emit 768 or 1024 dimensions against
  OpenAI's 1536. A Qdrant collection is created with a fixed vector size, so a mismatch is a
  collection you have to recreate, not a setting you can change.
- **Changing embedder invalidates the store.** Vectors produced by different models are not
  comparable, and nothing re-embeds silently on your behalf. Switching embedder means re-ingesting
  the corpus — including switching between a local model and a hosted one. Each embedder's
  `Identity` (an `EmbeddingModelIdentity`: provider, model, dimensions, revision) names the model, so
  a store can record it and refuse another.

See [17 — Local & self-hosted models](17-local-models.md) for the rest of the self-hosting picture.

## Persistent Store — Postgres

A host that already runs Postgres can keep its knowledge there, with pgvector (package `Ananke.Postgres`,
experimental: `ANNKEEXP004`). It does all three search modes: embedding (`Dense`), keyword (`Sparse`, Postgres
full-text search) and both fused (`Hybrid`):

```csharp
#pragma warning disable ANNKEEXP004
await using var dataSource = NpgsqlDataSource.Create(connectionString);
var store = new PostgresKnowledgeStore(dataSource, embeddingModel, new PostgresKnowledgeOptions { Table = "docs_chunks" });
var catalog = new PostgresKnowledgeCatalog(dataSource, embeddingModel);

var results = await store.SearchAsync(query, new SearchOptions { Mode = SearchMode.Hybrid, TopK = 8 });
```

Each table records which embedder wrote it, and a store with a different embedder is refused rather than
returning meaningless matches. See [`Ananke.Postgres`](../../src/Ananke.Postgres/README.md) for the tables and
options.

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
