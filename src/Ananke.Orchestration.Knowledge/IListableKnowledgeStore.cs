using System.Diagnostics.CodeAnalysis;

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
    /// The chunks whose metadata has every key and value of <paramref name="filter"/>, at most
    /// <paramref name="limit"/>. Their <see cref="KnowledgeChunk.Score"/> is 0.
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
}

/// <summary>
/// The order for <see cref="IListableKnowledgeStore.ListAsync"/>: newest first by the date in metadata key
/// <paramref name="Key"/>, an ISO 8601 date and time such as <c>DocumentHeaderOptions.EffectiveAtKey</c> holds. Chunks
/// without the key, or whose value isn't a date, come after the dated ones.
/// </summary>
/// <param name="Key">The metadata key holding each chunk's date.</param>
/// <param name="Since">When given, only chunks dated at or after it are listed; those without a date are left out.</param>
[Experimental(ExperimentalApis.KnowledgeBase, Message = ExperimentalApis.KnowledgeBaseMessage, UrlFormat = ExperimentalApis.UrlFormat)]
public sealed record KnowledgeDateOrder(string Key, DateTimeOffset? Since = null);
