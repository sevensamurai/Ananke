namespace Ananke.Orchestration.Agents.Context;

/// <summary>
/// Receives a <see cref="ContextObservation"/> for each model call that reports one.
/// </summary>
/// <remarks>
/// Optional and off by default: a workflow that installs no observer behaves exactly as before and
/// pays nothing. Implementations must tolerate concurrent calls — fork branches observe in parallel.
/// </remarks>
public interface IContextObserver
{
    /// <summary>Called once per model call, before the request is sent.</summary>
    ValueTask OnContextAssembledAsync(ContextObservation observation, CancellationToken ct = default);

    /// <summary>
    /// Called once per compaction, reporting the budget in force and what was withheld.
    /// </summary>
    /// <remarks>
    /// Defaulted so an observer that only cares about window pressure needs no ceremony to ignore it.
    /// </remarks>
    ValueTask OnContextCompactedAsync(ContextCompactionRecord record, CancellationToken ct = default) =>
        ValueTask.CompletedTask;

    /// <summary>
    /// Called when a tool result exceeded the configured cap and was spilled or truncated before
    /// it could enter the window.
    /// </summary>
    /// <remarks>
    /// Reported at the moment it happens rather than as part of the next assembly record: capping
    /// is what stops a payload entering the window, so by assembly time there is nothing left to
    /// see. Defaulted, like the compaction hook.
    /// </remarks>
    ValueTask OnToolOutputCappedAsync(ToolOutputCapRecord record, CancellationToken ct = default) =>
        ValueTask.CompletedTask;
}

/// <summary>An observer that discards everything. The default when none is installed.</summary>
public sealed class NullContextObserver : IContextObserver
{
    /// <summary>Shared singleton instance.</summary>
    public static NullContextObserver Instance { get; } = new();

    /// <inheritdoc />
    public ValueTask OnContextAssembledAsync(
        ContextObservation observation, CancellationToken ct = default) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask OnContextCompactedAsync(
        ContextCompactionRecord record, CancellationToken ct = default) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public ValueTask OnToolOutputCappedAsync(
        ToolOutputCapRecord record, CancellationToken ct = default) => ValueTask.CompletedTask;
}

/// <summary>
/// What one assembly decided, recorded once per compaction.
/// </summary>
/// <remarks>
/// Composed from the two types the seam already carries rather than restating their fields, so
/// there is one definition of a budget and one of a projection.
/// </remarks>
public sealed record ContextCompactionRecord
{
    /// <summary>The allocation and capacity in force, and where they came from.</summary>
    public required ContextBudget Budget { get; init; }

    /// <summary>What the strategy produced, and what it withheld.</summary>
    public required ContextProjection Projection { get; init; }

    /// <summary>How many messages went in, before compaction.</summary>
    public required int InputMessageCount { get; init; }

    /// <summary>
    /// How much of this assembly is the pinned contract — content compaction cannot reach, and
    /// therefore a floor under every request the job makes. Zero when no contract is in force.
    /// </summary>
    /// <remarks>
    /// Reported because it is the obvious way a pinning mechanism goes wrong: a contract that is
    /// pinned by construction is also paid for by construction, and one that quietly takes a third
    /// of a small model's window has traded one context problem for another. Worth watching against
    /// <see cref="ContextBudget.ModelContextTokens"/>.
    /// </remarks>
    public int ContractTokens { get; init; }

    /// <summary>
    /// What tool-result pruning reclaimed before the strategy ran, or <see langword="null"/> when
    /// no policy was configured or nothing was worth reducing.
    /// </summary>
    /// <remarks>
    /// Pruning belongs to the assembly rather than to the projection because it happens
    /// <em>before</em> the strategy and can remove the need for one entirely — which is the whole
    /// reason it runs first. Recording it on the projection would attribute the engine's work to
    /// the strategy.
    /// </remarks>
    public ToolOutputPruningRecord? Pruning { get; init; }
}
