namespace Ananke.Orchestration.Streaming;

/// <summary>
/// Extension methods for subscribing to specific <see cref="WorkflowEvent{TState}"/> types
/// from <see cref="Workflow{TState}.StreamAsync"/> without pattern matching boilerplate.
/// </summary>
/// <example>
/// <code>
/// await foreach (var completed in workflow.StreamAsync(state).OfType&lt;JobCompleted&lt;MyState&gt;&gt;())
/// {
///     Console.WriteLine($"Job {completed.JobName} took {completed.Duration}");
/// }
/// </code>
/// </example>
public static class WorkflowEventExtensions
{
    /// <summary>
    /// Filters the event stream to only events of type <typeparamref name="TEvent"/>.
    /// </summary>
    public static async IAsyncEnumerable<TEvent> OfType<TState, TEvent>(
        this IAsyncEnumerable<WorkflowEvent<TState>> events,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        where TEvent : WorkflowEvent<TState>
    {
        await foreach (var e in events.WithCancellation(ct))
        {
            if (e is TEvent typed)
                yield return typed;
        }
    }

    /// <summary>
    /// Invokes <paramref name="handler"/> for each event of type <typeparamref name="TEvent"/>
    /// and forwards all events unchanged.
    /// </summary>
    /// <example>
    /// <code>
    /// await foreach (var e in workflow.StreamAsync(state)
    ///     .On&lt;MyState, JobStarted&lt;MyState&gt;&gt;(e =&gt; Console.WriteLine($"Starting {e.JobName}"))
    ///     .On&lt;MyState, JobCompleted&lt;MyState&gt;&gt;(e =&gt; Console.WriteLine($"Done {e.JobName}")))
    /// {
    ///     // all events still flow through
    /// }
    /// </code>
    /// </example>
    public static async IAsyncEnumerable<WorkflowEvent<TState>> On<TState, TEvent>(
        this IAsyncEnumerable<WorkflowEvent<TState>> events,
        Action<TEvent> handler,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        where TEvent : WorkflowEvent<TState>
    {
        ArgumentNullException.ThrowIfNull(handler);

        await foreach (var e in events.WithCancellation(ct))
        {
            if (e is TEvent typed)
                handler(typed);

            yield return e;
        }
    }

    /// <summary>
    /// Invokes an async <paramref name="handler"/> for each event of type <typeparamref name="TEvent"/>
    /// and forwards all events unchanged.
    /// </summary>
    public static async IAsyncEnumerable<WorkflowEvent<TState>> OnAsync<TState, TEvent>(
        this IAsyncEnumerable<WorkflowEvent<TState>> events,
        Func<TEvent, Task> handler,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        where TEvent : WorkflowEvent<TState>
    {
        ArgumentNullException.ThrowIfNull(handler);

        await foreach (var e in events.WithCancellation(ct))
        {
            if (e is TEvent typed)
                await handler(typed);

            yield return e;
        }
    }
}
