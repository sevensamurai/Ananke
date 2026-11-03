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
    /// The chunks whose metadata has every key and value of <paramref name="filter"/>, ordered by id, at most
    /// <paramref name="limit"/>. Their <see cref="KnowledgeChunk.Score"/> is 0.
    /// </summary>
    Task<IReadOnlyList<KnowledgeChunk>> ListAsync(KnowledgeFilter filter, int limit = 1000, CancellationToken ct = default);
}
