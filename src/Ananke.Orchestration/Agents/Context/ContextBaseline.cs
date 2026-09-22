namespace Ananke.Orchestration.Agents.Context;

/// <summary>
/// What a run of workflows says about how the context window is actually being used.
/// </summary>
/// <remarks>
/// <para>
/// These are measurements of the <em>current</em> system, taken so that later claims about improving
/// it have something to be judged against. None of them is a target to optimise: several improve by
/// suppression — a workflow that does less work truncates less — so each is only meaningful against
/// an outcome held constant.
/// </para>
/// <para>
/// <b>Coverage is reported alongside every number that needs it.</b> Truncation and headroom are
/// undefined where the selected model's window is unknown, and a figure computed over three calls
/// out of four hundred is not a small sample, it is a different question.
/// </para>
/// </remarks>
public sealed record ContextBaseline
{
    /// <summary>Model calls observed.</summary>
    public required int Calls { get; init; }

    /// <summary>Of those, how many knew the selected model's real window.</summary>
    public required int CallsWithKnownWindow { get; init; }

    /// <summary>
    /// Share of calls that knew their window. Below this, the two figures that depend on it say
    /// nothing.
    /// </summary>
    public double WindowCoverage => Calls == 0 ? 0 : CallsWithKnownWindow / (double)Calls;

    /// <summary>
    /// Share of window-known calls whose assembled prompt exceeded that window.
    /// </summary>
    /// <remarks>
    /// The number that went from *not detectable at all* to measurable. Target zero, and it is a
    /// defect count rather than a tuning dial.
    /// </remarks>
    public double TruncationIncidence { get; init; }

    /// <summary>How full the window was at the 95th percentile, in <c>[0,1]</c>.</summary>
    /// <remarks>
    /// Percentiles, never the mean. Window pressure is a tail problem: a workflow that sits at 30%
    /// on average and 99% on its worst call fails on the worst call, and an average hides exactly
    /// that.
    /// </remarks>
    public double FillP95 { get; init; }

    /// <summary>How full the window was at the 99th percentile.</summary>
    public double FillP99 { get; init; }

    /// <summary>Trajectories observed.</summary>
    public required int Episodes { get; init; }

    /// <summary>Tool calls across all episodes.</summary>
    public required int ToolCalls { get; init; }

    /// <summary>
    /// Share of tool calls that repeated an earlier call in the same episode.
    /// </summary>
    /// <remarks>
    /// A proxy for having forgotten something already known. It is also the secondary signal a
    /// progress monitor would watch, so this baseline doubles as its calibration data.
    /// </remarks>
    public double ReworkRate { get; init; }

    /// <summary>Compactions observed.</summary>
    public required int Compactions { get; init; }

    /// <summary>Share of compactions that actually withheld something.</summary>
    public double CompactionRate { get; init; }

    /// <summary>Calls that carried a pinned contract.</summary>
    /// <remarks>
    /// Reported alongside the mean so that "no contract was pinned" and "a contract was pinned and
    /// cost nothing" stay distinguishable. They are different facts, and only one of them is a
    /// problem.
    /// </remarks>
    public int CallsWithContract { get; init; }

    /// <summary>
    /// Tokens per assembly held by a pinned contract, averaged over the calls that carried one.
    /// </summary>
    public double MeanContractTokens { get; init; }

    /// <summary>Renders the figures as a table, with the caveats attached to the ones that have them.</summary>
    public string ToReport()
    {
        var report = new System.Text.StringBuilder("Context baseline\n");
        report.Append("  calls observed                 ").Append(Calls).Append('\n');
        report.Append("  window known                   ")
            .Append(CallsWithKnownWindow).Append(" (").Append(Percent(WindowCoverage)).Append(")\n");

        if (CallsWithKnownWindow == 0)
        {
            report.Append("  truncation incidence           n/a — no call knew its window\n");
            report.Append("  fill p95 / p99                 n/a\n");
        }
        else
        {
            report.Append("  truncation incidence           ").Append(Percent(TruncationIncidence))
                .Append("   (target zero)\n");
            report.Append("  fill p95 / p99                 ").Append(Percent(FillP95))
                .Append(" / ").Append(Percent(FillP99)).Append('\n');
        }

        report.Append("  episodes                       ").Append(Episodes).Append('\n');
        report.Append("  tool calls                     ").Append(ToolCalls).Append('\n');
        report.Append("  rework rate                    ").Append(Percent(ReworkRate)).Append('\n');
        report.Append("  compactions                    ").Append(Compactions)
            .Append(" (").Append(Percent(CompactionRate)).Append(" withheld something)\n");
        report.Append("  pinned contract                ").Append(
            CallsWithContract == 0
                ? "none — no call carried one"
                : $"{MeanContractTokens:F0} tokens on {CallsWithContract} of {Calls} call(s)")
            .Append('\n');

        if (WindowCoverage < 1 && Calls > 0)
        {
            report.Append("\n  Note: ").Append(Calls - CallsWithKnownWindow)
                .Append(" call(s) had no known window. Truncation and fill are computed over the rest.\n");
        }

        return report.ToString();
    }

    private static string Percent(double value) => (value * 100).ToString("F1") + "%";
}
