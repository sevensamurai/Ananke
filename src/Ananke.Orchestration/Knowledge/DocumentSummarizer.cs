using Ananke.Orchestration.Agents;

namespace Ananke.Orchestration.Knowledge;

/// <summary>
/// Generates LLM-powered descriptions of processed documents. Use this to automatically
/// populate <see cref="ProcessingResult.Description"/> when a manual description is not
/// provided at ingest time.
/// </summary>
/// <remarks>
/// <para>
/// The generated description is designed to be used as a tool description for
/// <see cref="KnowledgeSearchTool"/> — it tells the agent model what domain the
/// knowledge base covers so it can decide autonomously when to search.
/// </para>
/// <para>
/// <b>Future:</b> Some embedding/ingestion providers (e.g. Azure AI Search, Google Vertex AI)
/// offer built-in document summarization as part of their indexing pipeline. When integrating
/// with such providers, prefer their native summarization over an extra LLM call.
/// </para>
/// </remarks>
public static class DocumentSummarizer
{
    private const string SummarizationPrompt =
        """
        You are a librarian cataloging a document. Given the text below, write a single 
        concise sentence describing what topics and domain this document covers. 
        The description will be used as a tool description for an AI assistant so it 
        knows when this knowledge base is relevant to a user's question.
        
        Focus on the specific subjects, techniques, and domain — not generic statements.
        Do NOT start with "This document..." — start directly with the subject matter.

        Example output:
        "Software refactoring techniques, code restructuring patterns, and the relationship 
        between iterative design and code quality, based on Martin Fowler's methodology."
        """;

    /// <summary>
    /// Uses an LLM to generate a description of the document content from its first chunks.
    /// Returns a new <see cref="ProcessingResult"/> with the <see cref="ProcessingResult.Description"/>
    /// populated.
    /// </summary>
    /// <param name="result">The processing result to describe.</param>
    /// <param name="model">The agent model to use for summarization.</param>
    /// <param name="store">The knowledge store to retrieve chunks from for context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A new <see cref="ProcessingResult"/> with the generated description.</returns>
    public static async Task<ProcessingResult> AutoDescribeAsync(
        this ProcessingResult result,
        IAgentModel model,
        IKnowledgeStore store,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(store);

        if (result.Chunks == 0)
            throw new InvalidOperationException(
                $"Cannot auto-describe '{result.Source}': the processing result contains 0 chunks. " +
                "Ensure ProcessAsync completed successfully before calling AutoDescribeAsync.");

        // Retrieve the first few chunks from this source to use as context
        var chunks = await store.SearchAsync(
            result.Source,
            new SearchOptions
            {
                TopK = 3,
                Filter = new KnowledgeFilter { ["source"] = result.Source }
            },
            ct);

        if (chunks.Count == 0)
            throw new InvalidOperationException(
                $"Cannot auto-describe '{result.Source}': no chunks found in the knowledge store " +
                $"for source '{result.Source}'. The document may not have been indexed correctly.");

        var sampleText = string.Join("\n\n---\n\n",
            chunks.Select(c => c.Text.Length > 1500 ? c.Text[..1500] : c.Text));

        var request = new AgentRequest
        {
            SystemPrompt = SummarizationPrompt,
            Messages = [AgentMessage.User(sampleText)]
        };

        var response = await model.GenerateAsync(request, ct);
        var description = response.Text?.Trim() ?? string.Empty;

        if (description.Length == 0)
            throw new InvalidOperationException(
                $"Auto-describe for '{result.Source}' returned an empty description. " +
                "The LLM model may have returned an empty or null response.");

        return result with { Description = description };
    }
}
