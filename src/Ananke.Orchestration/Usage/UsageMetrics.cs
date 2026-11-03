using System.Diagnostics.Metrics;
using Ananke.Abstractions;
using Ananke.Abstractions.Agents;
using Ananke.Abstractions.Budget;

namespace Ananke.Orchestration.Usage;

/// <summary>
/// Counters for what model calls consume, on the <see cref="AnankeSourceNames.OrchestrationUsage"/>
/// meter. Instrument and tag names are in <see cref="UsageInstrumentNames"/>.
/// </summary>
internal static class UsageMetrics
{
    private static readonly Meter Meter = new(AnankeSourceNames.OrchestrationUsage, "1.0.0");

    private static readonly Counter<long> TokensIn = Meter.CreateCounter<long>(
        UsageInstrumentNames.TokensIn, unit: "{token}", description: "Input tokens consumed by model calls.");

    private static readonly Counter<long> TokensOut = Meter.CreateCounter<long>(
        UsageInstrumentNames.TokensOut, unit: "{token}", description: "Output tokens produced by model calls.");

    private static readonly Counter<double> Cost = Meter.CreateCounter<double>(
        UsageInstrumentNames.Cost, description: "Cost of model calls priced per call, in the rates' currency.");

    internal static void PublishTokens(TokenUsage usage, string? workflow)
    {
        var tag = Tag(workflow);
        TokensIn.Add(usage.InputTokens, tag);
        TokensOut.Add(usage.OutputTokens, tag);
    }

    internal static void PublishCost(decimal cost, string? workflow) =>
        Cost.Add((double)cost, Tag(workflow));

    private static KeyValuePair<string, object?> Tag(string? workflow) =>
        new(UsageInstrumentNames.WorkflowTag, workflow);
}
