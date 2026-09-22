namespace Ananke.Orchestration.Streaming;

/// <summary>Somewhere for a workflow event to go.</summary>
/// <remarks>
/// One implementation ships — the one <see cref="Execution.IWorkflowRunner.StreamAsync{TState}"/>
/// installs over its own channel. It is an interface so that a test can collect events without a
/// stream, and so that nothing below the runner has to know a channel is involved.
/// </remarks>
public interface IWorkflowEventSink
{
    /// <summary>Reports <paramref name="evt"/>. Never throws because nobody is listening.</summary>
    ValueTask ReportAsync(WorkflowEvent evt, CancellationToken ct = default);
}

/// <summary>
/// Resolves the <see cref="IWorkflowEventSink"/> for the current async flow.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately the same shape as <c>UsageRecording</c> and <c>ContextObserving</c>: an
/// <see cref="AsyncLocal{T}"/> holding an <em>immutable service reference</em> rather than mutable
/// state a caller reaches through, a <see cref="BeginScope"/> that does not nest, and a report that
/// no-ops when nothing is scoped.
/// </para>
/// <para>
/// <b>The non-nesting rule is what makes this work rather than a detail of it.</b> A sub-workflow
/// builds its own runner, and that runner inherits the outer sink instead of shadowing it — so a
/// nested workflow's events reach the stream the caller is actually reading, which is exactly the
/// property that was missing when a sub-workflow's progress vanished between <c>JobStarted</c> and
/// <c>JobCompleted</c> of the job that contained it.
/// </para>
/// <para>
/// <b>And it is how work below a job reports at all.</b> A job cannot reach the runner's channel,
/// and the runner must not learn what any particular job does — a runner that special-cased one job
/// type would not have extended the event seam, it would have bypassed it. Reporting to whatever
/// sink is scoped leaves both sides ignorant of each other.
/// </para>
/// <para>
/// A job that reports outside a workflow, or inside one nobody is streaming, is not an error: the
/// report is dropped. That is what lets the same job run under <c>RunAsync</c> and
/// <c>StreamAsync</c> without knowing which.
/// </para>
/// </remarks>
public static class WorkflowEventReporting
{
    private static readonly AsyncLocal<IWorkflowEventSink?> Ambient = new();

    /// <summary>The sink for the current async flow, or <c>null</c> when none is active.</summary>
    public static IWorkflowEventSink? Current => Ambient.Value;

    /// <summary>
    /// Establishes <paramref name="sink"/> for the current async flow, unless one is already
    /// active — in which case the existing sink is kept and the returned scope restores nothing.
    /// </summary>
    public static Scope BeginScope(IWorkflowEventSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        // First one in owns the flow. A nested runner that shadowed the outer sink would send its
        // events somewhere nobody is reading, which is the defect this rule exists to prevent.
        if (Ambient.Value is not null)
            return new Scope(previous: Ambient.Value, restore: false);

        var previous = Ambient.Value;
        Ambient.Value = sink;
        return new Scope(previous, restore: true);
    }

    /// <summary>
    /// Reports <paramref name="evt"/> to the ambient sink. A no-op when none is scoped.
    /// </summary>
    public static ValueTask ReportAsync(WorkflowEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return Ambient.Value is not { } sink ? ValueTask.CompletedTask : sink.ReportAsync(evt, ct);
    }

    /// <summary>Restores the ambient sink that was in place before the scope began.</summary>
    public readonly struct Scope(IWorkflowEventSink? previous, bool restore) : IDisposable
    {
        /// <summary>Whether this scope actually installed a sink.</summary>
        public bool IsOwner => restore;

        /// <inheritdoc />
        public void Dispose()
        {
            if (restore)
                Ambient.Value = previous;
        }
    }
}
