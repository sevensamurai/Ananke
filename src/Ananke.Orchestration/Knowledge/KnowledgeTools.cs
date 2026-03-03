using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Tools;

namespace Ananke.Orchestration.Knowledge;

/// <summary>
/// Factory for creating a <see cref="ToolKit"/> with document processing and knowledge search tools.
/// The returned kit contains <c>process_document</c> and <c>search_knowledge</c> tools that
/// can be merged into any existing <see cref="ToolKit"/> via <see cref="ToolKit.Merge"/>.
/// </summary>
public static class KnowledgeTools
{
    /// <summary>
    /// Creates a <see cref="ToolKit"/> with <c>process_document</c> and a knowledge search tool.
    /// </summary>
    /// <param name="processor">The document processor for fetching, extracting, and indexing documents.</param>
    /// <param name="store">The knowledge store for searching indexed documents.</param>
    /// <param name="searchDescription">
    /// Description of what the knowledge base contains. Surfaced to the model as the search
    /// tool description so it can decide autonomously when to search.
    /// </param>
    /// <param name="defaultSearchOptions">Optional default search options (topK, threshold, filter).</param>
    /// <param name="formatting">
    /// Controls which metadata fields (source URI, page number) appear in formatted results.
    /// Default shows all available metadata.
    /// </param>
    /// <param name="describeModel">
    /// When provided, the <c>process_document</c> tool automatically generates an LLM summary
    /// of each ingested document via <see cref="DocumentSummarizer.AutoDescribeAsync"/>.
    /// When <see langword="null"/>, documents are indexed without a description.
    /// </param>
    public static ToolKit Create(
        DocumentProcessor processor,
        IKnowledgeStore store,
        string? searchDescription = null,
        SearchOptions? defaultSearchOptions = null,
        SearchResultFormatting? formatting = null,
        IAgentModel? describeModel = null)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(store);

        searchDescription ??= "Search the knowledge base for information from previously indexed documents.";
        formatting ??= new SearchResultFormatting();

        var processDescription = describeModel is not null
            ? "Fetch, process, and index a document from a URL (PDF, webpage, etc.) " +
              "and generate an LLM summary of its content."
            : "Fetch, process, and index a document from a URL (PDF, webpage, etc.) " +
              "so its content can be searched later.";

        return new ToolKit("knowledge")
            .AddTool(
                name: "process_document",
                description: processDescription,
                execute: async url =>
                {
                    var result = await processor.ProcessAsync(new Uri(url));

                    if (describeModel is not null)
                        result = await result.AutoDescribeAsync(describeModel, store);

                    return FormatProcessingResult(result);
                },
                paramName: "url",
                paramDescription: "The URL of the document to process and index")
            .AddTool(
                name: "search_knowledge",
                description: searchDescription,
                execute: async query =>
                {
                    var results = await store.SearchAsync(query, defaultSearchOptions);
                    return KnowledgeSearchTool.FormatResults(results, formatting);
                },
                paramName: "query",
                paramDescription: "Natural language search query");
    }

    private static string FormatProcessingResult(ProcessingResult result)
    {
        var message = $"Indexed {result.Chunks} chunks from {result.Source}.";

        if (result.Description is { Length: > 0 })
            message += $" Summary: {result.Description}";

        return message;
    }
}
