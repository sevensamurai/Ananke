using Ananke.Abstractions.Graph;
using Ananke.Orchestration.Knowledge;

namespace Ananke.Learning.Knowledge.Builders;

/// <summary>
/// Builds a document-structure graph from <see cref="KnowledgeDocument"/> instances
/// produced by an ingestion pipeline (e.g. a PDF or web-page source).
/// </summary>
/// <remarks>
/// <para>
/// Complements <see cref="TagCoOccurrenceBuilder"/> and
/// <see cref="EpisodeTrajectoryBuilder"/> for the document-ingestion path. Call
/// <see cref="AddDocumentAsync"/> for each document resolved by an
/// <c>ExternalKnowledgeSyncer</c> batch, then query the graph for structural signals
/// (entity hubs, citation chains, section clusters).
/// </para>
/// <para>
/// Node ID conventions:
/// <list type="bullet">
///   <item><c>doc:{KnowledgeDocument.Id}</c></item>
///   <item><c>entity:{value}</c> — derived from <see cref="KnowledgeDocument.Metadata"/> keys
///     prefixed with <c>entity:</c></item>
/// </list>
/// </para>
/// <para>
/// Edge conventions:
/// <list type="bullet">
///   <item><c>mentions</c> (doc → entity) — <see cref="EdgeProvenance.Extracted"/> when the
///     document metadata key starts with <c>entity:</c>; <see cref="EdgeProvenance.Ambiguous"/>
///     when inferred from the document title/content heuristic.</item>
///   <item><c>cites</c> (doc → doc) — <see cref="EdgeProvenance.Extracted"/> when the
///     metadata key <c>cites:{targetId}</c> is present.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class DocumentStructureBuilder
{
    private const string EntityMetaPrefix = "entity:";
    private const string CitesMetaPrefix  = "cites:";

    /// <summary>
    /// Processes a single resolved document and upserts the corresponding
    /// nodes and edges into <paramref name="graph"/>.
    /// </summary>
    public async Task AddDocumentAsync(
        IKnowledgeGraph graph,
        KnowledgeDocument document,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(document);

        var docNodeId = DocId(document.Id);

        await graph.UpsertNodeAsync(new GraphNode
        {
            Id   = docNodeId,
            Kind = "document",
        }, ct);

        foreach (var (key, value) in document.Metadata)
        {
            ct.ThrowIfCancellationRequested();

            if (key.StartsWith(EntityMetaPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var entityNodeId = $"entity:{value}";
                await graph.UpsertNodeAsync(new GraphNode { Id = entityNodeId, Kind = "entity" }, ct);
                await graph.UpsertEdgeAsync(new GraphEdge
                {
                    FromId     = docNodeId,
                    ToId       = entityNodeId,
                    Relation   = "mentions",
                    Provenance = EdgeProvenance.Extracted,
                }, ct);
            }
            else if (key.StartsWith(CitesMetaPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var citedDocId = DocId(value);
                await graph.UpsertNodeAsync(new GraphNode { Id = citedDocId, Kind = "document" }, ct);
                await graph.UpsertEdgeAsync(new GraphEdge
                {
                    FromId     = docNodeId,
                    ToId       = citedDocId,
                    Relation   = "cites",
                    Provenance = EdgeProvenance.Extracted,
                }, ct);
            }
        }
    }

    internal static string DocId(string id) => $"doc:{id}";
}
