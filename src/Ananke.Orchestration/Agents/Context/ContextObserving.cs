namespace Ananke.Orchestration.Agents.Context;

/// <summary>
/// Resolves the <see cref="IContextObserver"/> for the current async flow.
/// </summary>
/// <remarks>
/// Mirrors <see cref="Usage.UsageRecording"/> deliberately, including the rule that
/// <see cref="BeginScope"/> does not nest — the outermost scope owns the flow, so a sub-workflow's
/// calls are observed by the run that started it rather than disappearing into a shadowing scope.
/// The ambient holds an immutable service reference, never mutable state a caller reaches through.
/// </remarks>
public static class ContextObserving
{
    private static readonly AsyncLocal<IContextObserver?> Ambient = new();

    /// <summary>The observer for the current async flow, or <c>null</c> when none is active.</summary>
    public static IContextObserver? Current => Ambient.Value;

    /// <summary>
    /// Establishes <paramref name="observer"/> for the current async flow, unless one is already
    /// active — in which case the existing observer is kept and the returned scope restores nothing.
    /// </summary>
    public static Scope BeginScope(IContextObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);

        if (Ambient.Value is not null)
            return new Scope(previous: Ambient.Value, restore: false);

        var previous = Ambient.Value;
        Ambient.Value = observer;
        return new Scope(previous, restore: true);
    }

    /// <summary>
    /// Reports one observation to the ambient observer. A no-op when none is scoped, so a model
    /// call outside a workflow still works and costs nothing.
    /// </summary>
    internal static ValueTask ReportAsync(ContextObservation observation, CancellationToken ct = default) =>
        Ambient.Value is not { } observer
            ? ValueTask.CompletedTask
            : observer.OnContextAssembledAsync(observation, ct);

    /// <summary>Reports one compaction to the ambient observer. A no-op when none is scoped.</summary>
    internal static ValueTask ReportAsync(ContextCompactionRecord record, CancellationToken ct = default) =>
        Ambient.Value is not { } observer
            ? ValueTask.CompletedTask
            : observer.OnContextCompactedAsync(record, ct);

    /// <summary>Reports one capped tool result to the ambient observer. A no-op when none is scoped.</summary>
    internal static ValueTask ReportAsync(ToolOutputCapRecord record, CancellationToken ct = default) =>
        Ambient.Value is not { } observer
            ? ValueTask.CompletedTask
            : observer.OnToolOutputCappedAsync(record, ct);

    /// <summary>Restores the ambient observer that was in place before the scope began.</summary>
    public readonly struct Scope(IContextObserver? previous, bool restore) : IDisposable
    {
        /// <summary>Whether this scope actually installed an observer.</summary>
        public bool IsOwner => restore;

        /// <inheritdoc />
        public void Dispose()
        {
            if (restore)
                Ambient.Value = previous;
        }
    }
}
