# Ananke.Documents — Architecture

> Document extractors for the Ananke knowledge pipeline —
> PDF, Markdown, and plain text.

## Role

Implements `IDocumentExtractor` for common document formats.
Used by the knowledge pipeline (`DocumentProcessor` in `Ananke.Orchestration`)
to ingest documents into `IKnowledgeStore`.

---

## Start Here

Read these first — they're the package's entry points; the rest of this file is reference
detail to come back to.

1. `DocumentExtractorFactory` — resolves the correct extractor based on file extension;
   the entry point consumers call — `src/Ananke.Documents/DocumentExtractorFactory.cs`
2. `PdfExtractor` — `IDocumentExtractor` for PDF files via PdfPig — `src/Ananke.Documents/PdfExtractor.cs`
3. `MarkdownExtractor` — `IDocumentExtractor` for Markdown files via Markdig — `src/Ananke.Documents/MarkdownExtractor.cs`

---

## Dependencies

- `Ananke.Orchestration` (project)
- `PdfPig` (PDF parsing)
- `Markdig` (Markdown parsing)

## Key Types

| Type | Kind | Purpose |
|------|------|---------|
| `PdfExtractor` | Class | `IDocumentExtractor` for PDF files via PdfPig |
| `MarkdownExtractor` | Class | `IDocumentExtractor` for Markdown files via Markdig |
| `PlainTextExtractor` | Class | `IDocumentExtractor` for `.txt` files |
| `DocumentExtractorFactory` | Class | Resolves the correct extractor based on file extension |
