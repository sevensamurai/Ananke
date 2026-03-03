namespace Ananke.Orchestration.Routing;

/// <summary>
/// Describes a fan-in point where multiple parallel branches converge.
/// The <see cref="Merge"/> function reconciles branch states into a single state.
/// </summary>
public sealed class JoinDescriptor<TState>
{
    /// <summary>The job names whose completion triggers the join.</summary>
    public IReadOnlyList<string> Sources { get; }

    /// <summary>The job to execute after all sources complete and states are merged.</summary>
    public string Target { get; }

    /// <summary>Reconciles the final state from each branch into a single state for the target job.</summary>
    public Func<TState[], TState> Merge { get; }

    internal JoinDescriptor(string[] sources, string target, Func<TState[], TState> merge)
    {
        Sources = sources;
        Target = target;
        Merge = merge;
    }
}
