using Ananke.Abstractions.Agents;

namespace Ananke.Orchestration.Usage;

/// <summary>
/// Resolves the <see cref="IUsageRecorder"/> for the current async flow.
/// </summary>
/// <remarks>
/// The ambient mechanism is the same <see cref="AsyncLocal{T}"/> the old
/// <c>TokenUsageCapture</c> used, and it was never the problem — <c>SubFlowContext</c>
/// uses it correctly. The problem was what was stored in it: a <em>mutable accumulator</em>
/// callers assigned and reached through. Here the ambient holds an immutable service
/// reference, established once per execution and never reassigned per job, so fork
/// branches and nested runners inherit it rather than shadowing it.
/// <para>
/// There is no public setter. A scope is the only way to establish one, and
/// <see cref="BeginScope"/> deliberately does not nest: the outermost runner wins, which
/// is what lets a parent's budget see a sub-workflow's spend.
/// </para>
/// </remarks>
public static class UsageRecording
{
    private static readonly AsyncLocal<IUsageRecorder?> Ambient = new();

    /// <summary>
    /// The recorder for the current async flow, or <c>null</c> when none is active.
    /// </summary>
    public static IUsageRecorder? Current => Ambient.Value;

    /// <summary>
    /// Establishes <paramref name="recorder"/> for the current async flow, unless one is
    /// already active — in which case the existing recorder is kept and the returned scope
    /// restores nothing.
    /// </summary>
    /// <returns>A scope that restores the previous ambient value when disposed.</returns>
    public static Scope BeginScope(IUsageRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        // Nested runners (SubFlowJob builds its own WorkflowRunner) must not shadow the
        // outer recorder: that is exactly how sub-workflow spend became invisible to the
        // parent's budget. First one in owns the flow.
        if (Ambient.Value is not null)
            return new Scope(previous: Ambient.Value, restore: false);

        var previous = Ambient.Value;
        Ambient.Value = recorder;
        return new Scope(previous, restore: true);
    }

    /// <summary>
    /// Reports a model response's usage to the ambient recorder. A no-op when the response
    /// carries no usage or no recorder is scoped — the same tolerance the old
    /// <c>TokenUsageCapture.Accumulate</c> had, so a model call outside a workflow still works.
    /// </summary>
    internal static Task ReportAsync(AgentResponse response, CancellationToken ct = default) =>
        response.Usage is null || Ambient.Value is not { } recorder
            ? Task.CompletedTask
            : recorder.RecordUsageAsync(new UsageRecord(response.Usage), ct);

    /// <summary>
    /// Reports per-call cost <em>without</em> tokens, for a cost-resolving router.
    /// </summary>
    /// <remarks>
    /// Deliberately cost-only. Tokens are reported by whichever agent job or router made the
    /// call; <c>ModelRouter</c> contributes only the rate-derived cost on top, exactly as the
    /// old <c>AddCost</c> did. Sending <c>response.Usage</c> here as well would double-count
    /// every token that passes through a cost-resolving router.
    /// </remarks>
    internal static Task ReportCostAsync(decimal modelCost, CancellationToken ct = default) =>
        Ambient.Value is not { } recorder
            ? Task.CompletedTask
            : recorder.RecordUsageAsync(new UsageRecord(TokenUsage.Zero, modelCost), ct);

    /// <summary>Restores the ambient recorder that was in place before the scope began.</summary>
    public readonly struct Scope(IUsageRecorder? previous, bool restore) : IDisposable
    {
        /// <summary>Whether this scope actually installed a recorder.</summary>
        public bool IsOwner => restore;

        /// <inheritdoc />
        public void Dispose()
        {
            if (restore)
                Ambient.Value = previous;
        }
    }
}
