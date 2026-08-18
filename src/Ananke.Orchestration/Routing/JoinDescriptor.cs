namespace Ananke.Orchestration.Routing;

/// <summary>
/// Describes a fan-in point: the branch endpoints that must complete, the job to run afterwards,
/// and how to reconcile the branch states into one.
/// </summary>
public sealed class JoinDescriptor<TState>
{
    /// <summary>The job names whose completion triggers the join.</summary>
    public IReadOnlyList<string> Sources { get; }

    /// <summary>The job to execute after all sources complete and states are merged.</summary>
    public string Target { get; }

    /// <summary>
    /// Reconciles the final state from each branch into a single state for the target job.
    /// </summary>
    /// <remarks>
    /// Not used by the runner — <see cref="ContextMerge"/> is called instead, so that branch
    /// outcomes reach the callback. For a join declared with the <see cref="JoinContext{TState}"/>
    /// overload this property is an adapter that fabricates <b>no</b> outcomes, so a direct caller
    /// would see <c>HasFailures == false</c> even when a branch failed; kept internal instead of
    /// removed only because <see cref="ContextMerge"/> is built by wrapping it in the array-form
    /// constructor.
    /// </remarks>
    internal Func<TState[], TState> Merge { get; }

    /// <summary>
    /// The merge the runner actually invokes. Always populated: a join declared with the
    /// array-form callback wraps it, ignoring the outcomes.
    /// </summary>
    internal Func<JoinContext<TState>, TState> ContextMerge { get; }

    internal JoinDescriptor(string[] sources, string target, Func<TState[], TState> merge)
    {
        Sources = sources;
        Target = target;
        Merge = merge;
        ContextMerge = ctx => merge([.. ctx.States]);
    }

    internal JoinDescriptor(string[] sources, string target, Func<JoinContext<TState>, TState> merge)
    {
        Sources = sources;
        Target = target;
        ContextMerge = merge;
        Merge = states => merge(new JoinContext<TState>
        {
            States = states,
            Outcomes = []
        });
    }
}
