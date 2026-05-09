using Ananke.Orchestration.Workflows;

namespace Ananke.Orchestration;

/// <summary>
/// Type-safe reference to a job registered in a <see cref="Workflow{TState}"/>.
/// Obtained via the <c>out</c> overloads of
/// <see cref="Workflow{TState}.Job(string, System.Func{TState, System.Threading.CancellationToken, System.Threading.Tasks.Task{TState}}, out JobRef)"/>
/// and passed to <see cref="Workflow{TState}.Then(JobRef, JobRef)"/>,
/// <see cref="Workflow{TState}.Chain(JobRef[])"/>, and other connection methods
/// to catch typos at compile time instead of at workflow build/run time.
/// </summary>
/// <example>
/// <code>
/// var workflow = new Workflow&lt;MyState&gt;("pipeline")
///     .Job("research", researchFunc, out var research)
///     .Job("draft", draftFunc, out var draft)
///     .Then(research, draft)
///     .Then(draft, Workflow.EndRef);
/// </code>
/// </example>
public readonly record struct JobRef
{
    /// <summary>The job name this reference points to.</summary>
    public string Name { get; }

    internal JobRef(string name) => Name = name;

    /// <summary>
    /// Implicit conversion to <see langword="string"/> for interop with string-based APIs.
    /// </summary>
    public static implicit operator string(JobRef r) => r.Name;

    /// <inheritdoc />
    public override string ToString() => Name;
}
