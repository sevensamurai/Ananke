using System.Runtime.CompilerServices;
using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Knowledge;
using Ananke.Orchestration.Knowledge.Catalog;
using Ananke.Orchestration.Knowledge.Documents;
using Ananke.Orchestration.Knowledge.Embeddings;
using Ananke.Orchestration.Knowledge.Linking;

// These types were moved from Ananke.Orchestration.Agents to Ananke.Abstractions.Agents.
// TypeForwardedTo ensures binary and source compatibility for consumers that reference
// them via Ananke.Orchestration.
[assembly: TypeForwardedTo(typeof(IAgentModel))]
[assembly: TypeForwardedTo(typeof(IStreamingAgentModel))]
[assembly: TypeForwardedTo(typeof(AgentRequest))]
[assembly: TypeForwardedTo(typeof(AgentResponse))]
[assembly: TypeForwardedTo(typeof(AgentStreamChunk))]
[assembly: TypeForwardedTo(typeof(AgentTool))]
[assembly: TypeForwardedTo(typeof(AgentResponseFormat))]
[assembly: TypeForwardedTo(typeof(TokenUsage))]
[assembly: TypeForwardedTo(typeof(IEmbeddingModel))]

// These types were moved from Ananke.Orchestration.Knowledge to Ananke.Orchestration.Knowledge (separate assembly).
// TypeForwardedTo ensures existing consumers that reference them via Ananke.Orchestration still resolve.
[assembly: TypeForwardedTo(typeof(IKnowledgeStore))]
[assembly: TypeForwardedTo(typeof(InMemoryKnowledgeStore))]
[assembly: TypeForwardedTo(typeof(KnowledgeDocument))]
[assembly: TypeForwardedTo(typeof(KnowledgeChunk))]
[assembly: TypeForwardedTo(typeof(KnowledgeFilter))]
[assembly: TypeForwardedTo(typeof(SearchOptions))]
[assembly: TypeForwardedTo(typeof(SearchMode))]
[assembly: TypeForwardedTo(typeof(SearchResultFormatting))]
[assembly: TypeForwardedTo(typeof(KnowledgeBase))]
[assembly: TypeForwardedTo(typeof(ProcessingResult))]
[assembly: TypeForwardedTo(typeof(TimeDecay))]
[assembly: TypeForwardedTo(typeof(IKnowledgeCatalog))]
[assembly: TypeForwardedTo(typeof(InMemoryKnowledgeCatalog))]
[assembly: TypeForwardedTo(typeof(CatalogAwareKnowledgeStore))]
[assembly: TypeForwardedTo(typeof(CatalogEntry))]
[assembly: TypeForwardedTo(typeof(CatalogKeywordExtractor))]
[assembly: TypeForwardedTo(typeof(IDocumentExtractor))]
[assembly: TypeForwardedTo(typeof(IDocumentChunker))]
[assembly: TypeForwardedTo(typeof(DocumentProcessor))]
[assembly: TypeForwardedTo(typeof(DocumentSummarizer))]
[assembly: TypeForwardedTo(typeof(SlidingWindowChunker))]
[assembly: TypeForwardedTo(typeof(InMemoryEmbedder))]
[assembly: TypeForwardedTo(typeof(DocumentLinkExtractor))]
[assembly: TypeForwardedTo(typeof(DocumentLink))]
[assembly: TypeForwardedTo(typeof(IDocumentLinkGraph))]
[assembly: TypeForwardedTo(typeof(InMemoryDocumentLinkGraph))]
[assembly: TypeForwardedTo(typeof(LinkedKnowledgeStore))]
