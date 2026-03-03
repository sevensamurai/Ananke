namespace Ananke.Orchestration.Knowledge;

/// <summary>Result of processing a document through the knowledge pipeline.</summary>
/// <param name="Sections">Number of sections extracted from the document.</param>
/// <param name="Chunks">Number of chunks stored in the knowledge store.</param>
/// <param name="Source">The source identifier (typically the document URL or file path).</param>
/// <param name="Description">
/// A human- or LLM-generated description of the document's content and domain.
/// Used to build tool descriptions for agent integration so the model knows what
/// topics the knowledge base covers. Populate manually at ingest time, or generate
/// automatically via <see cref="DocumentSummarizer.AutoDescribeAsync"/>.
/// </param>
public sealed record ProcessingResult(
    int Sections,
    int Chunks,
    string Source,
    string Description = "");
