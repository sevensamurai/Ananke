using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

namespace Ananke.Orchestration.Knowledge.Documents;

/// <summary>
/// Several records steps given to <see cref="DocumentProcessor.Records"/> as one: one
/// <c>DatedRecordExtractor&lt;TRecord&gt;</c> per record type, say, each with its own model call, sharing a store when
/// each names its own <c>RecordType</c>. Each step is run on the document in turn. A step that throws doesn't stop the
/// ones after it: once every step has run, its exception is thrown, or an <see cref="AggregateException"/> holding each
/// when more than one threw.
/// </summary>
/// <remarks>
/// Steps whose <see cref="IDocumentRecords.Owns"/> would delete each other's records are refused: on one store, a
/// step that replaces every extracted record beside any other step that replaces records, or two steps that replace the
/// same record type. A store reached through <see cref="Linking.LinkedKnowledgeStore"/> or
/// <see cref="Catalog.CatalogAwareKnowledgeStore"/> counts as the store they decorate; past those, stores are told apart
/// by reference, so two store instances over one table or collection (two registrations, a transient one from DI, an
/// entity-scoped store or another decorator) are not told apart, and the steps on them are not refused.
/// </remarks>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed class CompositeDocumentRecords : IDocumentRecords
{
    private readonly IDocumentRecords[] _steps;

    /// <summary>Runs <paramref name="steps"/> in order.</summary>
    /// <exception cref="ArgumentException">
    /// A step is null, or two steps would delete each other's records (see <see cref="IDocumentRecords.Owns"/>).
    /// </exception>
    public CompositeDocumentRecords(params IDocumentRecords[] steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Any(step => step is null))
            throw new ArgumentException("A records step is null.", nameof(steps));
        _steps = [.. steps];
        Owns = [.. _steps.SelectMany(step => step.Owns)];
        WritesTo = [.. _steps.SelectMany(step => step.WritesTo)];

        foreach (var store in Owns.GroupBy(r => Decorated(r.Store), ReferenceEqualityComparer.Instance))
        {
            var name = store.Key!.GetType().Name;
            var types = store.Select(r => r.RecordType).ToList();
            if (types.Count > 1 && types.Contains(null))
                throw new ArgumentException(
                    $"A records step with no RecordType replaces every extracted record in its store ({name}), "
                    + "so it can't share that store with another extractor: give each its own RecordType.", nameof(steps));
            if (types.GroupBy(t => t, StringComparer.Ordinal).FirstOrDefault(t => t.Count() > 1) is { } same)
                throw new ArgumentException(
                    $"Two records steps replace the record type '{same.Key}' in one store ({name}): "
                    + "give each its own RecordType.", nameof(steps));
        }
    }

    /// <summary>The store <paramref name="store"/> decorates, through any linked or catalog-aware store.</summary>
    internal static IKnowledgeStore Decorated(IKnowledgeStore store) => store switch
    {
        Linking.LinkedKnowledgeStore linked => Decorated(linked.Inner),
        Catalog.CatalogAwareKnowledgeStore catalogAware => Decorated(catalogAware.Inner),
        _ => store,
    };

    /// <inheritdoc />
    /// <remarks>What each of the steps replaces.</remarks>
    public IReadOnlyList<(IKnowledgeStore Store, string? RecordType)> Owns { get; }

    /// <inheritdoc />
    /// <remarks>What each of the steps writes to.</remarks>
    public IReadOnlyList<IKnowledgeStore> WritesTo { get; }

    /// <inheritdoc />
    /// <returns>How many records the steps stored, and refused, together.</returns>
    public async Task<(int Stored, int Refused)> ExtractAsync(
        string sourceId, ExtractedDocument document, IReadOnlyDictionary<string, string> header, CancellationToken ct = default)
    {
        var (stored, refused) = (0, 0);
        List<Exception> failures = [];
        foreach (var step in _steps)
        {
            try
            {
                var result = await step.ExtractAsync(sourceId, document, header, ct).ConfigureAwait(false);
                stored += result.Stored;
                refused += result.Refused;
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                failures.Add(e);
            }
        }

        if (failures.Count == 1)
            ExceptionDispatchInfo.Throw(failures[0]);
        if (failures.Count > 1)
            throw new AggregateException($"{failures.Count} records steps failed for '{sourceId}'.", failures);
        return (stored, refused);
    }
}
