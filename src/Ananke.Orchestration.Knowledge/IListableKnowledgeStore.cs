using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Ananke.Orchestration.Knowledge;

/// <summary>
/// A store that can list what it holds by metadata, not only search it: every chunk whose metadata matches a filter, up
/// to a limit. A search returns the top few by relevance, so it can miss some of a set; a list returns them all, as a
/// roundup needs.
/// </summary>
/// <remarks>
/// <see cref="InMemoryKnowledgeStore"/>, <c>QdrantKnowledgeStore</c> and <c>PostgresKnowledgeStore</c> implement it. A
/// store that wraps another lists through it, and throws <see cref="NotSupportedException"/> when the store it wraps
/// can't list.
/// </remarks>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public interface IListableKnowledgeStore : IKnowledgeStore
{
    /// <summary>
    /// The chunks whose metadata has every key and value of <paramref name="filter"/> (its key <c>id</c> being the
    /// chunk's own id), at most <paramref name="limit"/>. Their <see cref="KnowledgeChunk.Score"/> is 0.
    /// </summary>
    /// <param name="filter">The metadata every chunk listed has.</param>
    /// <param name="limit">How many chunks to return at most.</param>
    /// <param name="order">
    /// Newest first by a date in each chunk's metadata, ordered and cut at <paramref name="limit"/> in the store's
    /// query. Without it, chunks come in the store's own order: by id, or by point id in <c>QdrantKnowledgeStore</c>.
    /// </param>
    /// <param name="ct">Cancels the listing.</param>
    Task<IReadOnlyList<KnowledgeChunk>> ListAsync(
        KnowledgeFilter filter, int limit = 1000, KnowledgeDateOrder? order = null, CancellationToken ct = default);

    /// <summary>
    /// Gives <paramref name="listed"/>, a chunk this store listed, <paramref name="metadata"/> in place of its metadata,
    /// keeping its text and its embedding (nothing is embedded again), only while the chunk is still stored with the
    /// text and metadata it was listed with. A chunk written again or deleted since it was listed is left as it is, so
    /// that write or delete is kept.
    /// </summary>
    /// <param name="listed">The chunk as <see cref="ListAsync"/> returned it.</param>
    /// <param name="metadata">The chunk's metadata from now on.</param>
    /// <param name="ct">Cancels the update.</param>
    Task UpdateMetadataAsync(KnowledgeChunk listed, IReadOnlyDictionary<string, string> metadata, CancellationToken ct = default);
}

/// <summary>
/// The order for <see cref="IListableKnowledgeStore.ListAsync"/>: newest first by the date in metadata key
/// <paramref name="Key"/>, an ISO 8601 date and time such as <c>DocumentHeaderOptions.EffectiveAtKey</c> holds, read as
/// UTC when it has no offset. Chunks without the key, or whose value isn't a date, come after the dated ones.
/// </summary>
/// <remarks>
/// A date is <c>yyyy-MM-dd</c>, optionally followed by <c>THH:mm</c>, seconds, up to seven digits of a fraction, and an
/// offset (<c>Z</c>, <c>+hh</c>, <c>+hh:mm</c> or <c>+hhmm</c>, at most 14 hours), within the range of
/// <see cref="DateTimeOffset"/> in UTC, as <see cref="ReadDate"/> reads it. Any other value, such as <c>infinity</c>,
/// <c>epoch</c>, <c>today</c> or <c>03/04/2026</c>, is no date, in every store.
/// <c>QdrantKnowledgeStore</c> lists by Qdrant's datetime index, which reads some values differently: as a date also a
/// space for the <c>T</c> (<c>2026-03-04 10:00:00</c>), a lowercase <c>t</c> or <c>z</c>, a space before the offset,
/// more than seven digits of a fraction and a one-digit month or day (<c>2026-3-4</c>); as no date a time without
/// seconds followed by an offset (<c>2026-03-04T10:00Z</c>).
/// </remarks>
/// <param name="Key">The metadata key holding each chunk's date.</param>
/// <param name="Since">When given, only chunks dated at or after it are listed; those without a date are left out.</param>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed partial record KnowledgeDateOrder(string Key, DateTimeOffset? Since = null)
{
    /// <summary>
    /// The date <paramref name="value"/> holds when it is an ISO 8601 date and time, read as UTC when it has no offset;
    /// otherwise <see langword="null"/>.
    /// </summary>
    /// <param name="value">A metadata value.</param>
    public static DateTimeOffset? ReadDate(string? value) =>
        value is not null
        && IsoDate().IsMatch(value)
        && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;

    // PostgresKnowledgeStore checks the same pattern in SQL: change both together.
    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}(T([01][0-9]|2[0-3]):[0-5][0-9](:[0-5][0-9](\.[0-9]{1,7})?)?(Z|[+-](0[0-9]|1[0-3])(:?[0-5][0-9])?|[+-]14(:?00)?)?)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDate();
}

/// <summary>Listing through a store that may not list.</summary>
internal static class KnowledgeStoreListing
{
    /// <summary>
    /// The chunks <paramref name="store"/> lists for <paramref name="filter"/>, at most <paramref name="limit"/>;
    /// <see langword="null"/> when it is not an <see cref="IListableKnowledgeStore"/> or wraps one that can't list.
    /// </summary>
#pragma warning disable ANNKEEXP004 // lists through the experimental listing
    internal static async Task<IReadOnlyList<KnowledgeChunk>?> TryListAsync(
        this IKnowledgeStore store, KnowledgeFilter filter, int limit, CancellationToken ct)
    {
        if (store is not IListableKnowledgeStore listable)
            return null;
        try
        {
            return await listable.ListAsync(filter, limit, ct: ct).ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Lists through <paramref name="inner"/>, the store a wrapper wraps, as <see cref="IListableKnowledgeStore.ListAsync"/>
    /// does; throws <see cref="NotSupportedException"/> when it is not an <see cref="IListableKnowledgeStore"/>.
    /// </summary>
    internal static Task<IReadOnlyList<KnowledgeChunk>> ListThroughAsync(
        this IKnowledgeStore inner, KnowledgeFilter filter, int limit, KnowledgeDateOrder? order, CancellationToken ct) =>
        inner is IListableKnowledgeStore listable
            ? listable.ListAsync(filter, limit, order, ct)
            : throw new NotSupportedException($"The store this one wraps ({inner.GetType().Name}) can't list.");

    /// <summary>
    /// Updates through <paramref name="inner"/>, the store a wrapper wraps, as
    /// <see cref="IListableKnowledgeStore.UpdateMetadataAsync"/> does; throws <see cref="NotSupportedException"/> when it
    /// is not an <see cref="IListableKnowledgeStore"/>.
    /// </summary>
    internal static Task UpdateMetadataThroughAsync(
        this IKnowledgeStore inner, KnowledgeChunk listed, IReadOnlyDictionary<string, string> metadata, CancellationToken ct) =>
        inner is IListableKnowledgeStore listable
            ? listable.UpdateMetadataAsync(listed, metadata, ct)
            : throw new NotSupportedException($"The store this one wraps ({inner.GetType().Name}) can't list.");
#pragma warning restore ANNKEEXP004
}
