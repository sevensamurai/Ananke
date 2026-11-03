using System.Runtime.CompilerServices;
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
/// <see cref="BeginScope(IUsageRecorder, string?)"/> deliberately does not nest: the outermost runner wins, which
/// is what lets a parent's budget see a sub-workflow's spend.
/// </para>
/// </remarks>
public static class UsageRecording
{
    private static readonly AsyncLocal<IUsageRecorder?> Ambient = new();

    private static readonly AsyncLocal<string?> AmbientWorkflow = new();

    // Responses whose cost a router has reported. Keyed by reference, so an entry lives only as
    // long as its response.
    private static readonly ConditionalWeakTable<AgentResponse, object> PricedResponses = [];

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
    public static Scope BeginScope(IUsageRecorder recorder) => BeginScope(recorder, workflow: null);

    /// <summary>
    /// Establishes <paramref name="recorder"/> for the current async flow, as
    /// <see cref="BeginScope(IUsageRecorder)"/> does, and names the <paramref name="workflow"/> whose
    /// calls it records. The name tags what the calls publish on the usage meter; when a scope is
    /// already active, its name is kept with its recorder.
    /// </summary>
    /// <returns>A scope that restores the previous ambient value when disposed.</returns>
    public static Scope BeginScope(IUsageRecorder recorder, string? workflow)
    {
        ArgumentNullException.ThrowIfNull(recorder);

        // Nested runners (SubFlowJob builds its own WorkflowRunner) must not shadow the
        // outer recorder: that is exactly how sub-workflow spend became invisible to the
        // parent's budget. First one in owns the flow.
        if (Ambient.Value is not null)
            return new Scope(previous: Ambient.Value, restore: false);

        var previous = Ambient.Value;
        Ambient.Value = recorder;
        AmbientWorkflow.Value = workflow;
        return new Scope(previous, restore: true);
    }

    /// <summary>
    /// Publishes a model response's tokens on the usage meter and reports them to the ambient
    /// recorder. A call outside a workflow publishes with no workflow tag and records nothing; a
    /// response with no usage does neither.
    /// </summary>
    /// <remarks>
    /// Tokens of a response a cost-resolving router has priced are recorded as
    /// <see cref="UsageRecord.Priced"/>, so a budget does not price them at flat rates as well.
    /// </remarks>
    internal static Task ReportAsync(AgentResponse response, CancellationToken ct = default)
    {
        if (response.Usage is null)
            return Task.CompletedTask;

        UsageMetrics.PublishTokens(response.Usage, AmbientWorkflow.Value);

        return Ambient.Value is { } recorder
            ? recorder.RecordUsageAsync(
                new UsageRecord(response.Usage, Priced: PricedResponses.TryGetValue(response, out _)), ct)
            : Task.CompletedTask;
    }

    /// <summary>
    /// Reports per-call cost <em>without</em> tokens, for a cost-resolving router, and marks
    /// <paramref name="response"/> as priced.
    /// </summary>
    /// <remarks>
    /// Deliberately cost-only. Tokens are reported by whichever agent job or router made the
    /// call, and a caller that uses the router directly reports none. Sending
    /// <c>response.Usage</c> here as well would double-count every token that passes through a
    /// cost-resolving router; the mark is what keeps the job's later token record from being
    /// priced a second time.
    /// </remarks>
    internal static Task ReportCostAsync(decimal modelCost, AgentResponse response, CancellationToken ct = default)
    {
        UsageMetrics.PublishCost(modelCost, AmbientWorkflow.Value);

        if (Ambient.Value is not { } recorder)
            return Task.CompletedTask;

        PricedResponses.AddOrUpdate(response, response);
        return recorder.RecordUsageAsync(new UsageRecord(TokenUsage.Zero, modelCost), ct);
    }

    /// <summary>Restores the ambient recorder that was in place before the scope began.</summary>
    public readonly struct Scope(IUsageRecorder? previous, bool restore) : IDisposable
    {
        /// <summary>Whether this scope actually installed a recorder.</summary>
        public bool IsOwner => restore;

        /// <inheritdoc />
        public void Dispose()
        {
            if (!restore)
                return;

            Ambient.Value = previous;
            AmbientWorkflow.Value = null;
        }
    }
}
