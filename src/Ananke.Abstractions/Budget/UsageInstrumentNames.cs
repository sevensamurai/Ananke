namespace Ananke.Abstractions.Budget;

/// <summary>
/// Instrument and tag names on the <see cref="AnankeSourceNames.OrchestrationUsage"/> meter.
/// </summary>
/// <remarks>
/// Every model call reports its tokens once. A call priced by a cost-resolving router also reports
/// its cost; a call with no per-call cost reports none, so <see cref="Cost"/> totals only what was
/// priced per call.
/// </remarks>
public static class UsageInstrumentNames
{
    /// <summary>Input tokens a model call consumed. A <c>long</c> counter.</summary>
    public const string TokensIn = "ananke.usage.tokens.in";

    /// <summary>Output tokens a model call produced. A <c>long</c> counter.</summary>
    public const string TokensOut = "ananke.usage.tokens.out";

    /// <summary>A call's cost at its model's rates, in the rates' currency. A <c>double</c> counter.</summary>
    public const string Cost = "ananke.usage.cost";

    /// <summary>
    /// The tag naming the workflow whose run made the call. A sub-workflow's calls carry the outermost
    /// workflow's name, as its budget does. Absent for a call made outside a workflow.
    /// </summary>
    public const string WorkflowTag = "workflow";
}
