# Ananke.Documents

[![NuGet](https://img.shields.io/nuget/v/Ananke.Documents.svg)](https://www.nuget.org/packages/Ananke.Documents)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)

Document extractors for the Ananke knowledge pipeline — `IDocumentExtractor` implementations for PDF and Markdown that feed into `DocumentProcessor` for ingestion, chunking, embedding, and vector storage.

## Install

```bash
dotnet add package Ananke.Documents
```

## Quick start

```csharp
using Ananke.Orchestration.Knowledge;
using Ananke.Documents;

var embeddingModel = OpenAIEmbeddingModel.Create(apiKey);
var knowledgeStore = new InMemoryKnowledgeStore(embeddingModel);

var processor = new DocumentProcessor(
    new HttpClient(),
    [new PdfExtractor(), new MarkdownExtractor()],
    new SlidingWindowChunker(),
    knowledgeStore);

// Extract, chunk, embed, and store in one call
await using var pdf = File.OpenRead("design-patterns.pdf");
var result = await processor.ProcessAsync(pdf, "application/pdf", "design-patterns");
// result => "8 sections, 42 chunks stored"
```

## Extractors

| Class | Input | What it does |
|---|---|---|
| `PdfExtractor` | `application/pdf` | Extracts text from PDF files using PdfPig, preserving headings, links, and structure as Markdown |
| `MarkdownExtractor` | `text/markdown`, `text/plain` | Parses Markdown structure into normalized sections suitable for chunking |

Both implement `IDocumentExtractor` — you can add your own by implementing the same interface.

## The pipeline

`DocumentProcessor` orchestrates the full ingest path:

```
Stream/URL → IDocumentExtractor → Markdown text
           → IDocumentChunker   → text chunks
           → IEmbeddingModel    → vector embeddings
           → IKnowledgeStore    → stored + indexed
```

The same processor works from agent tool calls, background jobs, admin scripts, or HTTP endpoints.

## Requirements

- `Ananke.Orchestration` (transitive) — provides `IDocumentExtractor`, `DocumentProcessor`, `IKnowledgeStore`, `SlidingWindowChunker`
- `PdfPig` ≥ 0.1.13 (transitive)
- `Markdig` ≥ 0.40.0 (transitive)

## Related packages

| Package | What it adds |
|---|---|
| `Ananke.Orchestration` | Core knowledge pipeline: `DocumentProcessor`, `IKnowledgeStore`, `InMemoryKnowledgeStore` |
| `Ananke.Orchestration.OpenAI` | `OpenAIEmbeddingModel` for generating embeddings |
| `Ananke.Qdrant` | Qdrant-backed `IKnowledgeStore` for persistent, distributed storage |
| `Ananke` | Meta-package — includes everything |

## Documentation

Full docs, demos, and architecture: **[github.com/sevensamurai/Ananke](https://github.com/sevensamurai/Ananke)**

## License

[Apache 2.0](https://github.com/sevensamurai/Ananke/blob/main/LICENSE)
