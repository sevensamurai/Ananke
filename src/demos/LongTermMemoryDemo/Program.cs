using Ananke.Documents;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Agents;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Documents;
using Ananke.Orchestration.Knowledge.Catalog;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Orchestration.Knowledge.Linking;
using Ananke.Orchestration.Knowledge.Tools;
using Ananke.Learning;
using Ananke.Orchestration.Memory;
using Ananke.Orchestration.OpenAI;
using Ananke.Qdrant;
using Microsoft.Extensions.Configuration;
using Qdrant.Client;
using System.Text;


// ---------------------------------------------------------------------
//  LongTermMemoryDemo — end-to-end knowledge pipeline:
//    1. Extract a local PDF into Markdown (PdfExtractor)
//    2. Chunk and embed into a knowledge store (in-memory or Qdrant)
//    3. Optionally maintain a knowledge catalog with LLM-enriched metadata
//    4. Optionally discover cross-document links via LLM classification
//    5. Loop through prompts in a single workflow with conversation memory:
//       - Ask an unrelated question (agent answers from its own knowledge)
//       - Ask about the indexed PDF (agent searches the knowledge base)
//       - Index a remote Markdown URL via process_external_url tool
//       - Ask about the newly indexed document (memory carries context)
//       - Ask a cross-document question (graph-expanded search with --linking)
//
//  Usage:
//    dotnet run                          ? in-memory store (no infra)
//    dotnet run -- --qdrant              ? Qdrant (requires docker compose up)
//    dotnet run -- --catalog             ? in-memory store + knowledge catalog
//    dotnet run -- --linking             ? in-memory store + cross-document linking
//    dotnet run -- --qdrant --catalog    ? Qdrant + catalog (full featured)
//    dotnet run -- --catalog --linking   ? catalog + linking (full pipeline)
//    docker compose up -d && dotnet run -- --qdrant --catalog --linking
// ---------------------------------------------------------------------

var useQdrant = args.Contains("--qdrant", StringComparer.OrdinalIgnoreCase);
var useCatalog = args.Contains("--catalog", StringComparer.OrdinalIgnoreCase);
var useLinking = args.Contains("--linking", StringComparer.OrdinalIgnoreCase);

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("secrets.json", optional: true)
    .Build();

var apiKey = config["OpenAI:ApiKey"]
    ?? throw new InvalidOperationException("OpenAI:ApiKey missing from secrets.json");
var modelName = config["OpenAI:Model"] ?? "gpt-4.1-mini";

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("-----------------------------------------------------------");
Console.WriteLine("  Ananke — Long-Term Memory Demo");
Console.WriteLine($"  Store: {(useQdrant ? "Qdrant (localhost:6334)" : "In-Memory")}");
if (useCatalog)
    Console.WriteLine("  Catalog: Enabled (LLM-enriched keywords + time decay)");
if (useLinking)
    Console.WriteLine("  Linking: Enabled (cross-document graph expansion)");
Console.WriteLine("-----------------------------------------------------------");
Console.WriteLine();

// -- Setup: embedding model + knowledge store ------------------------

var embeddingModel = OpenAIEmbeddingModel.Create(apiKey);

IKnowledgeStore knowledgeStore = useQdrant
    ? new QdrantKnowledgeStore(new QdrantClient("localhost", 6334), embeddingModel)
    : new InMemoryKnowledgeStore(embeddingModel);

// -- Optional: wrap with knowledge catalog for document-level discovery --

IKnowledgeCatalog? catalog = null;
if (useCatalog)
{
    catalog = useQdrant
        ? new QdrantKnowledgeCatalog(new QdrantClient("localhost", 6334), embeddingModel)
        : new InMemoryKnowledgeCatalog(embeddingModel);

    var extractor = new CatalogKeywordExtractor(OpenAIChatAgentModel.Create(apiKey, modelName));
    knowledgeStore = new CatalogAwareKnowledgeStore(
        knowledgeStore, catalog, extractor,
        new TimeDecayOptions { HalfLifeDays = 90, FloorWeight = 0.3f });
}

// -- Optional: wrap with cross-document linking for graph-expanded search --

IDocumentLinkGraph? linkGraph = null;
DocumentLinkExtractor? linkExtractor = null;
if (useLinking)
{
    linkGraph = new InMemoryDocumentLinkGraph();
    knowledgeStore = new LinkedKnowledgeStore(knowledgeStore, linkGraph,
        new LinkedSearchOptions { ExpansionSeeds = 3, MaxHops = 1, GraphScoreDiscount = 0.8f });
    linkExtractor = new DocumentLinkExtractor(
        OpenAIChatAgentModel.Create(apiKey, modelName), knowledgeStore, linkGraph);
}

var processor = new DocumentProcessor(
    new HttpClient(), [new PdfExtractor(), new MarkdownExtractor(), new PlainTextExtractor()], new SlidingWindowChunker(), knowledgeStore);

// -- Step 1: Ingest the PDF (extract ? chunk ? embed ? store) --------

Console.WriteLine("?? Processing refactoring.pdf...");

var pdfPath = Path.Combine(AppContext.BaseDirectory, "refactoring.pdf");
await using var pdfStream = File.OpenRead(pdfPath);
var result = await processor.ProcessAsync(
    pdfStream, ".pdf", "refactoring.pdf",
    tags: new Dictionary<string, string> { ["source_uri"] = pdfPath });

Console.WriteLine($"   ? {result.Sections} sections, {result.Chunks} chunks stored");

// Auto-describe: ask the LLM to summarize what the document covers
Console.WriteLine("   ? Generating document description...");
result = await result.AutoDescribeAsync(OpenAIChatAgentModel.Create(apiKey, modelName), knowledgeStore);
Console.WriteLine($"   ? \"{result.Description}\"");
Console.WriteLine();

// -- Show catalog contents (reusable helper) -------------------------

async Task ShowCatalogAsync()
{
    if (catalog is null) return;

    Console.WriteLine("?? Knowledge Catalog:");
    var entries = await catalog.BrowseAsync();
    foreach (var entry in entries)
    {
        Console.WriteLine($"   Source:   {entry.Source}");
        Console.WriteLine($"   Category: {entry.Category}");
        Console.WriteLine($"   Keywords: {string.Join(", ", entry.Keywords)}");
        Console.WriteLine($"   Summary:  {entry.Summary}");
        Console.WriteLine($"   Chunks:   {entry.ChunkCount} | Indexed: {entry.IndexedAt:yyyy-MM-dd HH:mm} UTC");
    }
    Console.WriteLine();
}

async Task ShowLinkGraphAsync()
{
    if (linkGraph is not InMemoryDocumentLinkGraph memGraph) return;

    Console.WriteLine($"?? Link Graph: {memGraph.LinkCount} cross-document link(s)");
    Console.WriteLine();
}

await ShowCatalogAsync();
await ShowLinkGraphAsync();

// -- Step 2: Single workflow loop with conversation memory ------------
//    The toolkit includes:
//    - search_knowledge: search the knowledge store (graph-expanded when --linking)
//    - process_external_url: fetch, extract, chunk, store, link, and auto-describe
//      a document from a URL (uses MarkdownExtractor + LLM summary)

var chatModel = OpenAIChatAgentModel.Create(apiKey, modelName);

var tools = KnowledgeSearchTool.Create("knowledge", knowledgeStore,
    description: "Search the knowledge base for information from all indexed documents.");

// Add process_external_url tool: fetch ? extract ? chunk ? store ? auto-describe
// NOTE: KnowledgeTools.Create(processor, store, describeModel: chatModel) does this
// out of the box as its built-in process_document tool.
// Done manually here only to demonstrate the full pipeline and hook into ShowCatalogAsync().
tools.AddTool(
    name: "process_external_url",
    description: "Fetch a document from a URL, extract its content, index it into the " +
                 "knowledge base, and generate an LLM summary. Use this when asked to " +
                 "index or import a URL. Returns the document summary and chunk count.",
    execute: async url =>
    {
        var processingResult = await processor.ProcessAsync(new Uri(url));
        processingResult = await processingResult.AutoDescribeAsync(
            OpenAIChatAgentModel.Create(apiKey, modelName), knowledgeStore);
        await ShowCatalogAsync();

        // Cross-document linking: discover relationships to existing chunks
        if (linkExtractor is not null && processingResult.Source is not null)
        {
            Console.WriteLine("   ?? Discovering cross-document links...");
            await linkExtractor.LinkSourceAsync(processingResult.Source);
            await ShowLinkGraphAsync();
        }

        return $"Indexed {processingResult.Chunks} chunks from {processingResult.Source}. " +
               $"Summary: {processingResult.Description}";
    },
    paramName: "url",
    paramDescription: "The URL of the document to fetch and index");

if (catalog is not null)
    tools.Merge(KnowledgeCatalogTools.Create(catalog));

var systemPrompt = """
    You are a senior software engineering assistant. You have access to a curated 
    knowledge base of reference materials. ALWAYS use search_knowledge to ground 
    your answers when the question relates to any previously indexed document — 
    do not rely on summaries or prior tool results alone. For general topics 
    outside the knowledge base, answer from your own expertise. When asked to 
    index or import a URL, use the process_external_url tool.
    """;

var prompts = new (string Label, string Text)[]
{
    ("Question 1 (unrelated to knowledge base)",
     "What are the main differences between TCP and UDP? Give me a short, concise answer."),

    ("Question 2 (relevant to indexed PDF)",
     "I have a large codebase that's becoming hard to maintain. What does the "
     + "literature say about when and how to approach restructuring existing code "
     + "without changing its behavior? Give me a short, concise answer."),

    ("Index a Markdown URL via tool",
     "Please index this URL: "
     + "https://raw.githubusercontent.com/dotnet/runtime/main/CONTRIBUTING.md"),

    ("Ask about the newly indexed document (uses conversation memory)",
     "What are the specific coding style rules and test requirements for contributing to the .NET runtime? Search the indexed document for details."),

    ("Cross-document question (graph-expanded search with --linking)",
     "How do the refactoring principles from the PDF relate to the contribution "
     + "guidelines for the .NET runtime? Are there overlapping themes around code "
     + "quality, testing, or incremental change? Search the knowledge base."),
};

var memory = new InMemoryConversationMemory();

var builder = StreamingChatWorkflow.Create("knowledge-demo", chatModel)
    .WithSystemPrompt(systemPrompt)
    .WithTools(tools)
    .WithMemory(memory)
    .OnTextDelta(async delta => Console.Write(delta))
    .OnToolCall(async (name, args) =>
    {
        Console.WriteLine();
        Console.WriteLine($"   ? Calling tool: {name}");
        Console.WriteLine($"      Args: {args}");
    })
    .OnToolResult(async (name, toolResult) =>
    {
        if (name == "process_external_url")
        {
            Console.WriteLine($"      ?? {toolResult}");
            await ShowCatalogAsync();
        }
        else
        {
            // Show source + score lines so we can see which documents matched
            foreach (var line in toolResult.Split('\n'))
            {
                if (line.StartsWith("Found") || line.StartsWith("No relevant")
                    || line.StartsWith("---") || line.StartsWith("Source:") || line.StartsWith("Page:"))
                    Console.WriteLine($"      {line}");
            }
        }
        Console.WriteLine();
    });

foreach (var (label, text) in prompts)
{
    Console.WriteLine($"-- {label} ------------------------------");
    Console.WriteLine($"   \"{text}\"");
    Console.WriteLine();

    var turn = await builder.RunAsync("session-1", [AgentMessage.User(text)]);

    Console.WriteLine();
    Console.WriteLine($"   ? Tool rounds: {turn.State.ToolRounds}");
    Console.WriteLine();
}

Console.WriteLine("-----------------------------------------------------------");
