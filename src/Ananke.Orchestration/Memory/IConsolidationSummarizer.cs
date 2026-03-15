using Ananke.Orchestration.Knowledge;

namespace Ananke.Orchestration.Memory;

/// <summary>
/// Generates a <see cref="KnowledgeDocument"/> from a mature empirical entry
/// that qualifies for promotion to <see cref="IKnowledgeStore"/>.
/// </summary>
/// <remarks>
/// Implementations range from deterministic template expansion
/// (<see cref="TemplateConsolidationSummarizer"/>) to LLM-powered
/// summarization. The offline learner calls this during the consolidation step.
/// </remarks>
public interface IConsolidationSummarizer
{
    /// <summary>
    /// Produces a knowledge document from an empirical entry.
    /// </summary>
    /// <param name="entry">The entry being consolidated.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A document ready for upsert into the knowledge store.</returns>
    Task<KnowledgeDocument> SummarizeAsync(EmpiricalEntry entry, CancellationToken ct = default);
}

/// <summary>
/// Deterministic, LLM-free consolidation summarizer that builds
/// <see cref="KnowledgeDocument"/> text from the entry's existing fields
/// via string interpolation. Suitable for tests, demos, and domains where
/// entries are already well-structured.
/// </summary>
public sealed class TemplateConsolidationSummarizer : IConsolidationSummarizer
{
    /// <inheritdoc />
    public Task<KnowledgeDocument> SummarizeAsync(EmpiricalEntry entry, CancellationToken ct = default)
    {
        var text = entry.Kind switch
        {
            EmpiricalKind.Pattern => FormatPattern(entry),
            EmpiricalKind.Skill => FormatSkill(entry),
            EmpiricalKind.Heuristic => FormatHeuristic(entry),
            _ => entry.Description.ToString()
        };

        var metadata = new Dictionary<string, string>
        {
            ["source_entry_id"] = entry.Id,
            ["source_kind"] = entry.Kind.ToString().ToLowerInvariant(),
            ["confidence_at_promotion"] = entry.Confidence.ToString("F2"),
            ["strength_at_promotion"] = entry.Strength.ToString("F2"),
            ["observation_count"] = entry.ObservationCount.ToString(),
            ["origin"] = "consolidation"
        };

        var doc = new KnowledgeDocument
        {
            Id = $"consolidated-{entry.Id}",
            Text = text,
            Metadata = metadata
        };

        return Task.FromResult(doc);
    }

    private static string FormatPattern(EmpiricalEntry e)
    {
        var parts = new List<string> { $"Pattern: {e.Description}" };
        if (e.Condition is not null) parts.Add($"Condition: {e.Condition}");
        if (e.Effect is not null) parts.Add($"Effect: {e.Effect}");
        if (e.Mechanism is not null) parts.Add($"Mechanism: {e.Mechanism}");
        if (e.Latency is not null) parts.Add($"Latency: {e.Latency.Value.TotalMinutes:F0} minutes");
        AppendEvidence(parts, e);
        return string.Join("\n", parts);
    }

    private static string FormatSkill(EmpiricalEntry e)
    {
        var parts = new List<string> { $"Skill: {e.Description}" };
        if (e.Goal is not null) parts.Add($"Goal: {e.Goal}");
        if (e.Applicability is not null) parts.Add($"Applicability: {e.Applicability}");
        if (e.Steps is { Count: > 0 })
        {
            parts.Add("Steps:");
            for (var i = 0; i < e.Steps.Count; i++)
                parts.Add($"  {i + 1}. {e.Steps[i]}");
        }
        AppendEvidence(parts, e);
        return string.Join("\n", parts);
    }

    private static string FormatHeuristic(EmpiricalEntry e)
    {
        var parts = new List<string> { $"Heuristic: {e.Description}" };
        if (e.Situation is not null) parts.Add($"Situation: {e.Situation}");
        if (e.PreferredApproach is not null) parts.Add($"Prefer: {e.PreferredApproach}");
        if (e.AvoidedApproach is not null) parts.Add($"Avoid: {e.AvoidedApproach}");
        AppendEvidence(parts, e);
        return string.Join("\n", parts);
    }

    private static void AppendEvidence(List<string> parts, EmpiricalEntry e)
    {
        if (e.Evidence is not { Count: > 0 }) return;
        var sample = e.Evidence.Take(3).ToList();
        parts.Add($"Evidence ({e.Evidence.Count} total): {string.Join("; ", sample)}");
    }
}
