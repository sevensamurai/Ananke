using System.Diagnostics.CodeAnalysis;

namespace Ananke.Orchestration.Knowledge.Documents;

/// <summary>
/// A step <see cref="DocumentProcessor"/> runs after a document's chunks are stored: it replaces the records a source
/// had with those read from its document. <c>DatedRecordExtractor&lt;TRecord&gt;</c> in <c>Ananke.Orchestration</c>
/// is the one that ships.
/// </summary>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public interface IDocumentRecords
{
    /// <summary>Replaces <paramref name="sourceId"/>'s records with those read from <paramref name="document"/>.</summary>
    /// <param name="sourceId">The document's source, as its chunks carry it.</param>
    /// <param name="document">The extracted document.</param>
    /// <param name="header">The fields its header gave, with <see cref="DocumentHeaderOptions.EffectiveAtKey"/>; empty when none were read.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>How many records were stored.</returns>
    Task<int> ExtractAsync(
        string sourceId, ExtractedDocument document, IReadOnlyDictionary<string, string> header, CancellationToken ct = default);
}
