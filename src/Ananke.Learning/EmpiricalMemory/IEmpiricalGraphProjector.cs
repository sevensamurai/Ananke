using Ananke.Abstractions.Graph;

namespace Ananke.Learning.EmpiricalMemory;

/// <summary>
/// Projects a committed <see cref="EmpiricalEntry"/> as a node (and optional edges)
/// into an <see cref="IKnowledgeGraph"/>.
/// </summary>
/// <remarks>
/// Register an implementation at the composition root to enable automatic graph
/// projection on every <see cref="IEmpiricalMemory.CommitAsync"/> call.
/// When no projector is registered the graph is left untouched — existing behaviour
/// is fully preserved.
/// </remarks>
public interface IEmpiricalGraphProjector
{
    /// <summary>
    /// Called after an entry is successfully committed to empirical memory.
    /// Implementations should upsert at minimum one <see cref="GraphNode"/>
    /// whose <c>Id</c> matches <paramref name="entry"/>'s
    /// <see cref="EmpiricalEntry.Id"/>.
    /// </summary>
    Task ProjectAsync(EmpiricalEntry entry, IKnowledgeGraph graph, CancellationToken ct = default);
}

/// <summary>
/// No-op default implementation — leaves the graph unchanged.
/// Used when no projector has been explicitly registered.
/// </summary>
public sealed class NullEmpiricalGraphProjector : IEmpiricalGraphProjector
{
    /// <summary>Singleton instance.</summary>
    public static readonly NullEmpiricalGraphProjector Instance = new();

    private NullEmpiricalGraphProjector() { }

    /// <inheritdoc />
    public Task ProjectAsync(EmpiricalEntry entry, IKnowledgeGraph graph, CancellationToken ct = default)
        => Task.CompletedTask;
}
