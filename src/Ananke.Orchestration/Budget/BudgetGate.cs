using Ananke.Abstractions.Agents;
using Ananke.Orchestration.Usage;

namespace Ananke.Orchestration.Budget;

/// <summary>Where an execution stands against its budget.</summary>
public enum BudgetState
{
    /// <summary>Under every configured threshold, or no budget is configured.</summary>
    Within,

    /// <summary>
    /// Past a warning threshold but not a limit. Reported to exactly one caller — the one whose
    /// evaluation crossed it — and never again for this execution.
    /// </summary>
    Warning,

    /// <summary>Past a limit. The run stops.</summary>
    Exceeded
}

/// <summary>Which ceiling a verdict refers to.</summary>
public enum BudgetLimitKind
{
    /// <summary>No threshold involved.</summary>
    None,

    /// <summary>This execution's own ceiling — <see cref="BudgetConfig.MaxCost"/>.</summary>
    Run,

    /// <summary>
    /// The period ceiling — <see cref="BudgetConfig.PeriodCostLimit"/> — accumulated across every
    /// run sharing the recorder, so it survives restarts.
    /// </summary>
    Period
}

/// <summary>The result of one budget evaluation.</summary>
/// <param name="RunTotals">This execution's usage, net of its baseline.</param>
/// <param name="RunCost">Cost of <paramref name="RunTotals"/>.</param>
/// <param name="PeriodCost">Cost accumulated over the whole period, across runs.</param>
/// <param name="State">Where the execution stands.</param>
/// <param name="Limit">Which ceiling produced <paramref name="State"/>.</param>
public readonly record struct BudgetVerdict(
    UsageSnapshot RunTotals,
    decimal RunCost,
    decimal PeriodCost,
    BudgetState State,
    BudgetLimitKind Limit);

/// <summary>
/// Evaluates an execution's spend against its budget. One instance per execution, shared by the
/// main path and every fork branch.
/// </summary>
/// <remarks>
/// Shared deliberately. The whole subject here is guarantees the main path had and the
/// branch path silently lacked; a second copy of this arithmetic in the branch loop would be the
/// next one to drift. Both paths ask the same object the same question.
/// <para>
/// Two ceilings come out of one read: the run's own spend is the total <em>net of the baseline</em>
/// taken when the execution started, while the period's is the raw total, which includes every
/// earlier run that shared the recorder. That is the whole difference between "this workflow cost
/// too much" and "the month is spent".
/// </para>
/// <para>Safe for concurrent callers: fork branches evaluate in parallel.</para>
/// </remarks>
/// <param name="recorder">Where every call in the run records its usage.</param>
/// <param name="baseline">The recorder's total when this runner started.</param>
/// <param name="budget">The ceilings, or <see langword="null"/> for none.</param>
/// <param name="carriedCost">What the execution spent before this runner started — a resumed run's
/// earlier legs, restored from its checkpoint.</param>
/// <param name="carriedUsage">The tokens those legs used.</param>
internal sealed class BudgetGate(
    IUsageRecorder recorder,
    UsageSnapshot baseline,
    BudgetConfig? budget,
    decimal carriedCost = 0m,
    TokenUsage? carriedUsage = null)
{
    // 0 = not yet warned. Interlocked rather than a lock so exactly one concurrent branch can
    // observe each crossing, which is what makes a warning fire once per execution.
    private int _warnedRun;
    private int _warnedPeriod;

    /// <summary>Whether a budget is configured at all.</summary>
    public bool HasBudget => budget is not null;

    /// <summary>The run ceiling, or <c>0</c> when none is configured.</summary>
    public decimal MaxCost => budget?.MaxCost ?? 0m;

    /// <summary>The period ceiling, if one is configured.</summary>
    public decimal? PeriodCostLimit => budget?.PeriodCostLimit;

    /// <summary>The threshold that produced a <see cref="BudgetState.Warning"/>, for reporting.</summary>
    public decimal WarnThresholdFor(BudgetLimitKind limit) => limit switch
    {
        BudgetLimitKind.Run => budget?.WarnAtCost ?? 0m,
        BudgetLimitKind.Period => budget?.WarnAtPeriodCost ?? 0m,
        _ => 0m
    };

    /// <summary>The ceiling that produced a verdict, for reporting.</summary>
    public decimal LimitFor(BudgetLimitKind limit) => limit switch
    {
        BudgetLimitKind.Run => MaxCost,
        BudgetLimitKind.Period => budget?.PeriodCostLimit ?? 0m,
        _ => 0m
    };

    /// <summary>Reads current totals and reports where the execution stands.</summary>
    public async Task<BudgetVerdict> EvaluateAsync(CancellationToken ct)
    {
        // One read, two questions. Re-reading for the period figure would let the two answers
        // disagree while branches are recording.
        var absolute = await recorder.ReadAsync(ct).ConfigureAwait(false);
        var sinceStart = absolute.Since(baseline);
        var runTotals = carriedUsage is { } carried
            ? sinceStart with { Usage = sinceStart.Usage.Add(carried) }
            : sinceStart;

        if (budget is null)
            return new BudgetVerdict(runTotals, 0m, 0m, BudgetState.Within, BudgetLimitKind.None);

        var runCost = carriedCost + CostOf(sinceStart);
        var periodCost = budget.PeriodCostLimit is null ? 0m : CostOf(absolute);

        // A limit beats a warning, and the period beats the run: being out of budget for the
        // month is the more consequential fact, and the one an operator needs named.
        if (budget.PeriodCostLimit is { } periodLimit && periodCost > periodLimit)
            return new BudgetVerdict(runTotals, runCost, periodCost, BudgetState.Exceeded, BudgetLimitKind.Period);

        if (runTotals.Usage.TotalTokens > 0 && runCost > budget.MaxCost)
            return new BudgetVerdict(runTotals, runCost, periodCost, BudgetState.Exceeded, BudgetLimitKind.Run);

        if (budget.WarnAtPeriodCost is { } periodWarn && periodCost > periodWarn &&
            Interlocked.CompareExchange(ref _warnedPeriod, 1, 0) == 0)
        {
            return new BudgetVerdict(runTotals, runCost, periodCost, BudgetState.Warning, BudgetLimitKind.Period);
        }

        if (budget.WarnAtCost is { } runWarn && runCost > runWarn &&
            Interlocked.CompareExchange(ref _warnedRun, 1, 0) == 0)
        {
            return new BudgetVerdict(runTotals, runCost, periodCost, BudgetState.Warning, BudgetLimitKind.Run);
        }

        return new BudgetVerdict(runTotals, runCost, periodCost, BudgetState.Within, BudgetLimitKind.None);
    }

    /// <summary>
    /// Reported per-call cost, plus flat rates for whatever arrived without one.
    /// </summary>
    /// <remarks>
    /// Deliberately additive rather than a choice between two whole-total strategies. The
    /// earlier form — <c>HasModelBasedCost ? AccumulatedCost : EstimateCost(Usage)</c> — meant a
    /// single routed job flipped the flag and the flat-rate branch was never taken again, so
    /// every plain-model job in the same workflow spent invisibly. EstimateCost is linear in
    /// tokens, so pricing the uncosted remainder in one go equals summing it per job.
    /// </remarks>
    private decimal CostOf(UsageSnapshot snapshot) =>
        snapshot.AccumulatedCost + budget!.EstimateCost(snapshot.UncostedUsage);
}
