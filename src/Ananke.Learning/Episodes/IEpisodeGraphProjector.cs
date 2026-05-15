using Ananke.Abstractions.Graph;

namespace Ananke.Learning.Episodes;

/// <summary>
/// Projects a committed <see cref="Episode"/> as nodes and edges into an
/// <see cref="IKnowledgeGraph"/>.
/// </summary>
/// <remarks>
/// Register an implementation at the composition root to enable automatic graph
/// projection on every <see cref="IEpisodeStore.CommitAsync"/> call.
/// When no projector is registered the graph is left untouched — existing behaviour
/// is fully preserved.
/// </remarks>
public interface IEpisodeGraphProjector
{
    /// <summary>
    /// Called after an episode is successfully committed to the episode store.
    /// Implementations should upsert at minimum one <see cref="GraphNode"/>
    /// whose <c>Id</c> matches <paramref name="episode"/>'s
    /// <see cref="Episode.Id"/>, and may add step nodes and causal edges.
    /// </summary>
    Task ProjectAsync(Episode episode, IKnowledgeGraph graph, CancellationToken ct = default);
}

/// <summary>
/// No-op default implementation — leaves the graph unchanged.
/// Used when no projector has been explicitly registered.
/// </summary>
public sealed class NullEpisodeGraphProjector : IEpisodeGraphProjector
{
    /// <summary>Singleton instance.</summary>
    public static readonly NullEpisodeGraphProjector Instance = new();

    private NullEpisodeGraphProjector() { }

    /// <inheritdoc />
    public Task ProjectAsync(Episode episode, IKnowledgeGraph graph, CancellationToken ct = default)
        => Task.CompletedTask;
}
