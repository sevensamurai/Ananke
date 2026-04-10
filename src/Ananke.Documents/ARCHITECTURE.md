# Ananke.Documents — Architecture

> Document extractors for the Ananke knowledge pipeline —
> PDF, Markdown, and plain text.

## Role

Implements `IDocumentExtractor` for common document formats.
Used by the knowledge pipeline (`DocumentProcessor` in `Ananke.Orchestration`)
to ingest documents into `IKnowledgeStore`.

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
