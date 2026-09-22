using System.Text;
using Ananke.Orchestration.Agents.Context;

namespace Ananke.Learning.EmpiricalMemory;

/// <summary>
/// Renders recalled entries at a chosen depth, and states which of them may be contradicted.
/// </summary>
/// <remarks>
/// <para>
/// One definition of what an entry costs. Both the allocator and the caller that finally emits the
/// text measure with this, so "will it fit" and "what was sent" cannot drift apart — the same
/// mistake as having two token estimators, made one layer up.
/// </para>
/// <para>
/// Token estimates come from the shared counter rather than a local heuristic, for the same reason.
/// </para>
/// </remarks>
public static class EmpiricalRecallRenderer
{
    /// <summary>Marks an entry the reader may contradict on what it observes now.</summary>
    public const string AdvisoryTag = "[advisory]";

    /// <summary>Marks an entry carrying the verdict of a check that was run.</summary>
    public const string RecordTag = "[verified record]";

    /// <summary>Renders one match at <paramref name="depth"/>.</summary>
    public static string Render(EmpiricalMatch match, RecallDepth depth)
    {
        ArgumentNullException.ThrowIfNull(match);

        var entry = match.Entry;
        var text = new StringBuilder();

        text.Append("--- [").Append(entry.Kind).Append("] ").Append(entry.Id)
            .Append(" (score: ").Append(match.Score.ToString("F3"))
            .Append(", confidence: ").Append(entry.Confidence.ToString("F2")).Append(") ")
            .Append(Authority(entry)).AppendLine(" ---");

        text.AppendLine(entry.Description.ToString());

        if (depth == RecallDepth.Abstract)
            return text.ToString();

        if (entry.Tags.Count > 0)
            text.Append("Tags: ").AppendLine(string.Join(", ", entry.Tags));

        // Whether the entry applies here — the fields a reader needs to decide that, and no more.
        Append(text, "Condition", entry.Condition);
        Append(text, "Effect", entry.Effect);
        Append(text, "Goal", entry.Goal);
        Append(text, "Applicable when", entry.Applicability);
        Append(text, "Situation", entry.Situation);
        Append(text, "Prefer", entry.PreferredApproach);
        Append(text, "Avoid", entry.AvoidedApproach);

        if (depth == RecallDepth.Entry)
            return text.ToString();

        // How and why — the expensive half, loaded only when the allocation reaches it.
        Append(text, "Mechanism", entry.Mechanism);
        if (entry.Latency is { } latency)
            text.Append("Latency: ").Append(latency.TotalMinutes.ToString("F0")).AppendLine(" minutes");

        if (entry.Steps is { Count: > 0 })
        {
            text.AppendLine("Steps:");
            for (var i = 0; i < entry.Steps.Count; i++)
                text.Append("  ").Append(i + 1).Append(". ").AppendLine(entry.Steps[i]);
        }

        Append(text, "Expected outcome", entry.ExpectedOutcome);
        if (entry.Tools is { Count: > 0 })
            text.Append("Tools: ").AppendLine(string.Join(", ", entry.Tools));
        if (entry.Evidence.Count > 0)
            text.Append("Evidence: ").AppendLine(string.Join(", ", entry.Evidence));

        text.Append("Observed: ").Append(entry.ObservationCount)
            .Append(" time(s) | Source: ").Append(entry.Source)
            .Append(" | Last seen: ").Append(entry.LastObserved.ToString("yyyy-MM-dd HH:mm"))
            .AppendLine(" UTC");

        return text.ToString();
    }

    /// <summary>Estimated token cost of rendering <paramref name="match"/> at <paramref name="depth"/>.</summary>
    public static int EstimateTokens(EmpiricalMatch match, RecallDepth depth) =>
        ApproximateTokenCounter.Instance.EstimateTokens(Render(match, depth));

    /// <summary>
    /// The one-off preamble telling the reader what it may do with each half of what follows.
    /// Only the halves actually present are described.
    /// </summary>
    /// <remarks>
    /// Stated once for the whole set rather than per entry: the rule is the same for every entry of
    /// a given half, and repeating it would spend the budget the rest of this type exists to save.
    /// </remarks>
    public static string RenderAuthorityNote(bool hasAdvisory, bool hasRecords)
    {
        var text = new StringBuilder();

        if (hasAdvisory)
        {
            text.Append(AdvisoryTag).AppendLine(
                " entries are prior experience. If what you observe now contradicts one, trust what "
                + "you observe — no escalation needed.");
        }

        if (hasRecords)
        {
            text.Append(RecordTag).AppendLine(
                " entries carry the verdict of a check that was actually run. Do not discard one on "
                + "judgement alone; supersede it only by re-running that check and recording the "
                + "new result.");
        }

        return text.ToString();
    }

    private static string Authority(EmpiricalEntry entry) =>
        entry.Verification is { } v
            ? $"{RecordTag} {(v.Passed ? "passed" : "failed")} \"{v.Oracle}\" on {v.VerifiedAt:yyyy-MM-dd}"
            : AdvisoryTag;

    private static void Append(StringBuilder text, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            text.Append(label).Append(": ").AppendLine(value);
    }
}
