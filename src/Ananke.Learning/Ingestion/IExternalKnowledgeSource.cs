using Ananke.Orchestration.Knowledge;

namespace Ananke.Learning.Ingestion;

/// <summary>
/// Domain-specific source of external knowledge that should be pre-materialized
/// into <see cref="IKnowledgeStore"/> so that agents can correlate runtime
/// observations with resolved external context without live API calls.
/// </summary>
/// <remarks>
/// <para>
/// This is the recommended integration point for products that need to feed
/// domain-specific context into the framework. The framework provides
/// <see cref="ExternalKnowledgeSyncer{TEvent}"/> to orchestrate the write pattern;
/// products implement this interface to resolve domain-specific data from
/// external systems (GitHub, supplier APIs, device registries, etc.).
/// </para>
/// <para>
/// Each call to <see cref="ResolveAsync"/> should produce a self-contained
/// batch of knowledge documents derived from a single external event
/// (e.g., a release, a shipment, a firmware update). The syncer handles
/// idempotent upsert via <see cref="IKnowledgeStore.UpsertAsync"/>.
/// </para>
/// <para>
/// <b>Domain examples:</b>
/// </para>
/// <list type="table">
///   <listheader>
///     <term>Product</term>
///     <description>External source</description>
///   </listheader>
///   <item>
///     <term>Backlog tool</term>
///     <description>GitHub Release → commits → PRs → cards</description>
///   </item>
///   <item>
///     <term>Digital marketplace</term>
///     <description>Supplier shipment → quality reports → SKU history</description>
///   </item>
///   <item>
///     <term>IoT fleet manager</term>
///     <description>Firmware release → patches → known issues → device groups</description>
///   </item>
/// </list>
/// </remarks>
/// <typeparam name="TEvent">
/// The domain event type that triggers resolution (e.g., a release event,
/// a shipment notification, a firmware update record). This is an opaque type
/// to the framework — only the product's implementation knows its structure.
/// </typeparam>
public interface IExternalKnowledgeSource<in TEvent>
{
    /// <summary>
    /// Resolves a domain event into knowledge documents suitable for storage
    /// in <see cref="IKnowledgeStore"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implementations should resolve the full join chain from the external
    /// system (e.g., release tag → GitHub API → commits → PRs → cards) and
    /// produce documents with searchable text and metadata.
    /// </para>
    /// <para>
    /// This method should be safe to call multiple times for the same event
    /// (idempotent). The syncer relies on <see cref="IKnowledgeStore.UpsertAsync"/>
    /// overwrite semantics.
    /// </para>
    /// </remarks>
    /// <param name="event">The domain event to resolve.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The resolved batch of documents. Return an empty batch
    /// (via <see cref="ResolvedKnowledgeBatch.Empty"/>) if the event
    /// should be skipped.
    /// </returns>
    Task<ResolvedKnowledgeBatch> ResolveAsync(TEvent @event, CancellationToken ct = default);
}

/// <summary>
/// A batch of resolved external knowledge ready to be written to
/// <see cref="IKnowledgeStore"/>.
/// </summary>
public sealed record ResolvedKnowledgeBatch
{
    /// <summary>
    /// Knowledge documents to upsert into <see cref="IKnowledgeStore"/>.
    /// Each document contains searchable text and metadata derived from
    /// the external event.
    /// </summary>
    public required IReadOnlyList<KnowledgeDocument> Documents { get; init; }

    /// <summary>An empty batch — use when an event should be skipped.</summary>
    public static ResolvedKnowledgeBatch Empty { get; } = new()
    {
        Documents = []
    };
}
