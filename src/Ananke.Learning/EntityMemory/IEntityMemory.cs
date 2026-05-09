using Ananke.Abstractions.Memory;
using Ananke.Learning.Episodes;
using Ananke.Orchestration.Knowledge;


using Ananke.Learning.EmpiricalMemory;

namespace Ananke.Learning.EntityMemory;

/// <summary>
/// Unified memory facade for a single entity (user, customer, device, household, etc.).
/// Composes conversation, empirical, knowledge, and episode memory with consistent
/// entity-scoped key partitioning.
/// </summary>
/// <remarks>
/// <para>
/// Each property exposes a standard Ananke memory interface scoped to the entity.
/// The underlying stores remain shared (single Qdrant collection, single Redis instance);
/// scoping is achieved via metadata filtering and key prefixing, not physical partitioning.
/// </para>
/// <para>
/// Obtain instances via <see cref="IEntityMemoryProvider.GetOrCreate"/>. Do not
/// implement this interface directly — use the default <c>EntityMemoryProvider</c> or
/// a custom <see cref="IEntityMemoryProvider"/>.
/// </para>
/// </remarks>
public interface IEntityMemory
{
    /// <summary>The entity this memory instance is scoped to.</summary>
    string EntityId { get; }

    /// <summary>
    /// Conversation history scoped to this entity. Session IDs are automatically
    /// prefixed with the entity ID to ensure isolation.
    /// </summary>
    IConversationMemory Conversations { get; }

    /// <summary>
    /// Empirical knowledge scoped to this entity. Commits inject
    /// <see cref="EmpiricalEntry.EntityId"/>, recalls filter by it.
    /// </summary>
    IEmpiricalMemory Empirical { get; }

    /// <summary>
    /// Semantic knowledge scoped to this entity. Upserts inject entity
    /// metadata, searches filter by it.
    /// </summary>
    IKnowledgeStore Knowledge { get; }

    /// <summary>
    /// Episode history scoped to this entity. Commits inject
    /// <see cref="Episodes.Episode.EntityId"/>, browsing filters by it.
    /// </summary>
    IEpisodeStore Episodes { get; }
}
