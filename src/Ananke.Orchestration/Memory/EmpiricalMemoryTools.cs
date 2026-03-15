using System.Text;
using Ananke.Orchestration.Tools;

namespace Ananke.Orchestration.Memory;

/// <summary>
/// Factory for creating a <see cref="ToolKit"/> that exposes empirical memory operations
/// to agents: recall known patterns/skills/heuristics, commit new insights, and reinforce
/// entries that proved correct. Follows the same factory pattern as
/// <see cref="Ananke.Orchestration.Knowledge.KnowledgeSearchTool"/>.
/// </summary>
public static class EmpiricalMemoryTools
{
    /// <summary>
    /// Creates a <see cref="ToolKit"/> with <c>recall_empirical</c>, <c>commit_insight</c>,
    /// and <c>reinforce_empirical</c> tools backed by an <see cref="IEmpiricalMemory"/>.
    /// </summary>
    /// <param name="memory">The empirical memory store to expose to agents.</param>
    /// <param name="name">Name for the returned <see cref="ToolKit"/>. Default is <c>"empirical"</c>.</param>
    /// <param name="affectOptions">Optional affect options for configurable initial confidence and other thresholds.</param>
    /// <param name="recallDescription">Description for the recall tool.</param>
    /// <param name="commitDescription">Description for the commit tool.</param>
    /// <param name="reinforceDescription">Description for the reinforce tool.</param>
    public static ToolKit Create(
        IEmpiricalMemory memory,
        string name = "empirical",
        AffectOptions? affectOptions = null,
        string? recallDescription = null,
        string? commitDescription = null,
        string? reinforceDescription = null)
    {
        ArgumentNullException.ThrowIfNull(memory);
        var affect = affectOptions ?? new AffectOptions();

        recallDescription ??=
            "Search empirical memory for known patterns, investigation skills, " +
            "and heuristics relevant to the current situation. Returns entries ranked " +
            "by relevance, confidence, and recency.";

        commitDescription ??=
            "Store a newly discovered pattern, learned procedure, or heuristic in " +
            "empirical memory for future recall. If a similar entry already exists, " +
            "it will be reinforced instead of duplicated.";

        reinforceDescription ??=
            "Reinforce a recalled empirical entry that proved correct or effective. " +
            "This increases its confidence and records that it was confirmed.";

        return new ToolKit(name)
            .AddTool(
                name: "recall_empirical",
                description: recallDescription,
                execute: async situation =>
                {
                    var results = await memory.RecallAsync(situation);
                    return FormatRecallResults(results);
                },
                paramName: "situation",
                paramDescription:
                    "Describe the current situation or problem to find relevant " +
                    "patterns, skills, and heuristics")
            .AddTool(
                name: "commit_insight",
                description: commitDescription,
                execute: async (description, kind) =>
                {
                    if (!TryParseKind(kind, out var parsedKind))
                        return ToolResult.Error(
                            $"Invalid kind '{kind}'. Must be one of: pattern, skill, heuristic.");

                    var entry = new EmpiricalEntry
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Kind = parsedKind,
                        Tags = [],
                        Source = "agent-committed",
                        Description = SemanticDescription.FromText(description),
                        Confidence = affect.InitialCommitConfidence,
                        ObservationCount = 1,
                        Evidence = [],
                        FirstObserved = DateTimeOffset.UtcNow,
                        LastObserved = DateTimeOffset.UtcNow
                    };

                    var committed = await memory.CommitAsync(entry);
                    return ToolResult.Ok(
                        $"Entry committed (id: {committed.Id}, kind: {committed.Kind}, " +
                        $"confidence: {committed.Confidence:F2}, observations: {committed.ObservationCount}).");
                },
                param1: ("description", "Natural language description of the pattern, skill, or heuristic to remember"),
                param2: ("kind", "Type of empirical knowledge: 'pattern' (observed correlation), 'skill' (procedure), or 'heuristic' (rule of thumb)"))
            .AddTool(
                name: "reinforce_empirical",
                description: reinforceDescription,
                execute: async entryId =>
                {
                    try
                    {
                        await memory.ReinforceAsync(entryId, new Reinforcement
                        {
                            NewEvidence = [],
                            Source = "agent-confirmed"
                        });

                        var updated = await memory.GetAsync(entryId);
                        return updated is not null
                            ? ToolResult.Ok(
                                $"Entry reinforced (id: {entryId}, " +
                                $"confidence: {updated.Confidence:F2}, " +
                                $"observations: {updated.ObservationCount}).")
                            : ToolResult.Ok($"Entry reinforced (id: {entryId}).");
                    }
                    catch (KeyNotFoundException)
                    {
                        return ToolResult.Error($"Empirical entry '{entryId}' not found.");
                    }
                },
                paramName: "entry_id",
                paramDescription: "The ID of the empirical entry to reinforce");
    }

    internal static string FormatRecallResults(IReadOnlyList<EmpiricalMatch> matches)
    {
        if (matches.Count == 0)
            return "No relevant experience found in memory.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {matches.Count} relevant experience(s):");
        sb.AppendLine();

        foreach (var match in matches)
        {
            var entry = match.Entry;
            sb.AppendLine($"--- [{entry.Kind}] {entry.Id} (score: {match.Score:F3}, confidence: {entry.Confidence:F2}) ---");
            sb.AppendLine(entry.Description.ToString());

            if (entry.Tags.Count > 0)
                sb.AppendLine($"Tags: {string.Join(", ", entry.Tags)}");

            if (entry.Condition is not null)
                sb.AppendLine($"Condition: {entry.Condition}");
            if (entry.Effect is not null)
                sb.AppendLine($"Effect: {entry.Effect}");
            if (entry.Latency is not null)
                sb.AppendLine($"Latency: {entry.Latency.Value.TotalMinutes:F0} minutes");

            if (entry.Goal is not null)
                sb.AppendLine($"Goal: {entry.Goal}");
            if (entry.Steps is { Count: > 0 })
            {
                sb.AppendLine("Steps:");
                for (var i = 0; i < entry.Steps.Count; i++)
                    sb.AppendLine($"  {i + 1}. {entry.Steps[i]}");
            }

            if (entry.Situation is not null)
                sb.AppendLine($"Situation: {entry.Situation}");
            if (entry.PreferredApproach is not null)
                sb.AppendLine($"Prefer: {entry.PreferredApproach}");
            if (entry.AvoidedApproach is not null)
                sb.AppendLine($"Avoid: {entry.AvoidedApproach}");

            sb.AppendLine(
                $"Observed: {entry.ObservationCount} time(s) | " +
                $"Source: {entry.Source} | " +
                $"Last seen: {entry.LastObserved:yyyy-MM-dd HH:mm} UTC");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static bool TryParseKind(string kind, out EmpiricalKind result) =>
        Enum.TryParse(kind, ignoreCase: true, out result);
}
