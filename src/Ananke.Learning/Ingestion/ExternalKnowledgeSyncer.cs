using Ananke.Orchestration.Knowledge;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ananke.Learning.Ingestion;

/// <summary>
/// Orchestrates writing resolved external knowledge into
/// <see cref="IKnowledgeStore"/>. Products provide an
/// <see cref="IExternalKnowledgeSource{TEvent}"/> that resolves domain events;
/// this syncer handles the write pattern, error isolation, and result reporting.
/// </summary>
/// <remarks>
/// <para>
/// This is the recommended way to feed domain-specific context into the
/// framework's knowledge layer. The syncer ensures:
/// </para>
/// <list type="bullet">
///   <item>Documents are upserted to <see cref="IKnowledgeStore"/> (idempotent).</item>
///   <item>Individual document failures don't abort the batch.</item>
///   <item>Results are reported for observability.</item>
/// </list>
/// <para>
/// <b>Typical usage:</b> Call <see cref="SyncAsync"/> from a webhook handler,
/// a CI event listener, or a scheduled worker.
/// </para>
/// </remarks>
/// <typeparam name="TEvent">The domain event type resolved by the source.</typeparam>
public sealed class ExternalKnowledgeSyncer<TEvent>(
    IExternalKnowledgeSource<TEvent> source,
    IKnowledgeStore knowledgeStore,
    ILogger<ExternalKnowledgeSyncer<TEvent>>? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<ExternalKnowledgeSyncer<TEvent>>.Instance;

    /// <summary>
    /// Resolves a single domain event and upserts the resulting documents
    /// into <see cref="IKnowledgeStore"/>.
    /// </summary>
    /// <param name="event">The domain event to resolve and sync.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A summary of what was written.</returns>
    public async Task<SyncResult> SyncAsync(TEvent @event, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        ResolvedKnowledgeBatch batch;
        try
        {
            batch = await source.ResolveAsync(@event, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "External knowledge source failed to resolve event");
            return SyncResult.Failed(ex);
        }

        if (batch.Documents.Count == 0)
        {
            _logger.LogDebug("External knowledge source returned empty batch — skipping");
            return SyncResult.Skipped;
        }

        var documentsUpserted = 0;
        var documentsFailed = 0;

        foreach (var doc in batch.Documents)
        {
            try
            {
                await knowledgeStore.UpsertAsync([doc], ct);
                documentsUpserted++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                documentsFailed++;
                _logger.LogWarning(ex, "Failed to upsert knowledge document {DocId}", doc.Id);
            }
        }

        var result = new SyncResult
        {
            DocumentsUpserted = documentsUpserted,
            DocumentsFailed = documentsFailed
        };

        _logger.LogInformation(
            "External knowledge sync: {Upserted} documents ({DocFails} failures)",
            documentsUpserted, documentsFailed);

        return result;
    }

    /// <summary>
    /// Resolves and syncs a batch of domain events. Each event is processed
    /// independently — a failure in one does not abort the batch.
    /// </summary>
    /// <param name="events">The domain events to resolve and sync.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An aggregated summary of all sync operations.</returns>
    public async Task<SyncResult> SyncBatchAsync(
        IEnumerable<TEvent> events, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        var totalDocs = 0;
        var totalDocFails = 0;
        var totalSkipped = 0;
        var totalFailed = 0;

        foreach (var @event in events)
        {
            ct.ThrowIfCancellationRequested();

            var result = await SyncAsync(@event, ct);

            if (result.IsSkipped)
            {
                totalSkipped++;
                continue;
            }

            if (result.Error is not null)
            {
                totalFailed++;
                continue;
            }

            totalDocs += result.DocumentsUpserted;
            totalDocFails += result.DocumentsFailed;
        }

        var aggregate = new SyncResult
        {
            DocumentsUpserted = totalDocs,
            DocumentsFailed = totalDocFails,
            EventsSkipped = totalSkipped,
            EventsFailed = totalFailed
        };

        _logger.LogInformation(
            "External knowledge batch sync complete: {Docs} documents, {Skipped} skipped, {Failed} failed",
            totalDocs, totalSkipped, totalFailed);

        return aggregate;
    }
}

/// <summary>
/// Summary of an external knowledge sync operation.
/// </summary>
public sealed record SyncResult
{
    /// <summary>Number of knowledge documents successfully upserted.</summary>
    public required int DocumentsUpserted { get; init; }

    /// <summary>Number of knowledge documents that failed to upsert.</summary>
    public required int DocumentsFailed { get; init; }

    /// <summary>Number of events skipped (source returned empty batch).</summary>
    public int EventsSkipped { get; init; }

    /// <summary>Number of events that failed during resolution.</summary>
    public int EventsFailed { get; init; }

    /// <summary>
    /// When set, the resolution itself failed. Individual document
    /// failures are reported via <see cref="DocumentsFailed"/> instead.
    /// </summary>
    public Exception? Error { get; init; }

    /// <summary>Whether this result represents a skipped event.</summary>
    public bool IsSkipped => DocumentsUpserted == 0 && DocumentsFailed == 0
                             && Error is null;

    /// <summary>Whether all operations succeeded with no failures.</summary>
    public bool IsFullySuccessful => DocumentsFailed == 0
                                     && EventsFailed == 0 && Error is null;

    internal static SyncResult Skipped { get; } = new()
    {
        DocumentsUpserted = 0,
        DocumentsFailed = 0
    };

    internal static SyncResult Failed(Exception error) => new()
    {
        DocumentsUpserted = 0,
        DocumentsFailed = 0,
        Error = error
    };
}
