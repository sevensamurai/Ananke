using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Ananke.Orchestration.Knowledge.Catalog;

namespace Ananke.Orchestration.Knowledge.Documents;

/// <summary>A document another document links to, as <see cref="LinkedDocumentReader"/> lists it.</summary>
/// <param name="Source">Its source id.</param>
/// <param name="EffectiveAt">Its own date, from the catalog or its chunks' header; <see langword="null"/> when it has none.</param>
/// <param name="IndexedAt">When it was indexed, from the catalog; <see langword="null"/> without one.</param>
/// <param name="SupersededBy">What supersedes it, from the catalog or its header; else <see langword="null"/>.</param>
/// <param name="Indexed">Whether the store holds it: a link can point at a document that was never indexed.</param>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed record LinkedDocument(
    string Source, DateTimeOffset? EffectiveAt, DateTimeOffset? IndexedAt, string? SupersededBy, bool Indexed);

/// <summary>
/// Lists the documents a document links to, from the links <see cref="DocumentProcessor.Hyperlinks"/> stored on its
/// chunks (<c>entity:document:{source id}</c>), with no model call. Each is dated and labelled with what supersedes it:
/// from the catalog when there is one and it has the document, else from the document's own chunks.
/// </summary>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class LinkedDocumentReader
{
    private readonly IListableKnowledgeStore _store;
    private readonly IKnowledgeCatalog? _catalog;

    /// <summary>Reads links from <paramref name="store"/>, the store <see cref="DocumentProcessor"/> writes.</summary>
    /// <param name="store">The chunks, with the links stored on them.</param>
    /// <param name="catalog">The catalog, for each linked document's date and what supersedes it; optional.</param>
    public LinkedDocumentReader(IListableKnowledgeStore store, IKnowledgeCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _catalog = catalog;
    }

    /// <summary>
    /// The distinct documents <paramref name="source"/> links to, newest first by their own date, else when they were
    /// indexed; those with neither last, then by source id. Empty when it links to none, or isn't indexed.
    /// </summary>
    /// <param name="source">The source id of the document whose links are listed.</param>
    /// <param name="maxChunks">How many of its chunks are read at most.</param>
    /// <param name="ct">Cancels the listing.</param>
    public async Task<IReadOnlyList<LinkedDocument>> ListAsync(string source, int maxChunks = 1000, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChunks);

        var chunks = await _store.ListAsync(new KnowledgeFilter { ["source"] = source }, maxChunks, ct: ct).ConfigureAwait(false);
        var targets = chunks
            .SelectMany(c => c.Metadata.Keys)
            .Where(k => k.StartsWith(HyperlinkOptions.KeyPrefix, StringComparison.Ordinal) && k.Length > HyperlinkOptions.KeyPrefix.Length)
            .Select(k => k[HyperlinkOptions.KeyPrefix.Length..])
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var linked = new List<LinkedDocument>(targets.Count);
        foreach (var target in targets)
            linked.Add(await DescribeAsync(target, ct).ConfigureAwait(false));

        return [.. linked
            .OrderByDescending(d => (d.EffectiveAt ?? d.IndexedAt).HasValue)
            .ThenByDescending(d => d.EffectiveAt ?? d.IndexedAt)
            .ThenBy(d => d.Source, StringComparer.Ordinal)];
    }

    /// <summary>A linked document's date and what supersedes it: the catalog's when it has the document, else its chunk's.</summary>
    private async Task<LinkedDocument> DescribeAsync(string target, CancellationToken ct)
    {
        var chunk = (await _store.ListAsync(new KnowledgeFilter { ["source"] = target }, 1, ct: ct).ConfigureAwait(false))
            .FirstOrDefault();
        var entry = _catalog is null ? null : await _catalog.GetAsync(target, ct).ConfigureAwait(false);

        return new LinkedDocument(
            target,
            entry?.EffectiveAt ?? Date(chunk),
            entry?.IndexedAt,
            entry?.SupersededBy ?? chunk?.Metadata.GetValueOrDefault(DocumentHeaderOptions.SupersededByKey),
            Indexed: chunk is not null || entry is not null);
    }

    private static DateTimeOffset? Date(KnowledgeChunk? chunk) =>
        chunk is not null
        && chunk.Metadata.TryGetValue(DocumentHeaderOptions.EffectiveAtKey, out var text)
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;
}
