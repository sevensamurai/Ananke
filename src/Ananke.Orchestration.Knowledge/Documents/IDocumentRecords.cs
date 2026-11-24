using System.Diagnostics.CodeAnalysis;

namespace Ananke.Orchestration.Knowledge.Documents;

/// <summary>
/// A step <see cref="DocumentProcessor"/> runs after a document's chunks are stored, on the records kept for its source.
/// Two ship in <c>Ananke.Orchestration</c>: <c>DatedRecordExtractor&lt;TRecord&gt;</c> replaces the records it read
/// from the document before, and <c>DatedRecordWriter&lt;TRecord&gt;</c> checks the quotes of the records written for
/// the source.
/// </summary>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public interface IDocumentRecords
{
    /// <summary>Brings <paramref name="sourceId"/>'s records up to date with <paramref name="document"/>.</summary>
    /// <param name="sourceId">The document's source, as its chunks carry it.</param>
    /// <param name="document">The extracted document.</param>
    /// <param name="header">The fields its header gave, with <see cref="DocumentHeaderOptions.EffectiveAtKey"/>; empty when none were read.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// How many records read from the document were stored, and how many were read but refused (not stored, such as
    /// a record whose quote isn't in the document).
    /// </returns>
    Task<(int Stored, int Refused)> ExtractAsync(
        string sourceId, ExtractedDocument document, IReadOnlyDictionary<string, string> header, CancellationToken ct = default);

    /// <summary>
    /// The records this step deletes and stores again each time a document is indexed: each store, with the record type
    /// it replaces there, or <see langword="null"/> for every extracted record of the source in that store. Empty for a
    /// step that deletes none. <see cref="CompositeDocumentRecords"/> refuses steps whose records would delete each other's.
    /// </summary>
    IReadOnlyList<(IKnowledgeStore Store, string? RecordType)> Owns { get; }

    /// <summary>
    /// Every store this step writes to, deletes from or marks records in. <see cref="DocumentProcessor"/> refuses a step
    /// that writes to its own store, where each indexing deletes every chunk of the source, records included.
    /// </summary>
    IReadOnlyList<IKnowledgeStore> WritesTo { get; }
}
