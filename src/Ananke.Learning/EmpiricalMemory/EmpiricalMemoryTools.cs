using System.Text;
using Ananke.Orchestration.Tools;

namespace Ananke.Learning.EmpiricalMemory;

/// <summary>
/// Factory for creating a <see cref="ToolKit"/> that exposes empirical memory operations
/// to agents: recall known patterns/skills/heuristics, commit new insights, and reinforce
/// entries that proved correct. Follows the same factory pattern as
/// <see cref="Ananke.Orchestration.Knowledge.Tools.KnowledgeSearchTool"/>.
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
    /// <param name="recallOptions">
    /// Options the recall tool searches with, including the token allocation recalled entries may
    /// occupy. The allocation is <em>supplied</em> — this library does not know which model was
    /// selected — so a caller that knows the window passes a share of it in here. When
    /// <see langword="null"/>, recall stays count-budgeted, as before.
    /// </param>
    public static ToolKit Create(
        IEmpiricalMemory memory,
        string name = "empirical",
        AffectOptions? affectOptions = null,
        string? recallDescription = null,
        string? commitDescription = null,
        string? reinforceDescription = null,
        RecallOptions? recallOptions = null)
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
                    var options = recallOptions ?? new RecallOptions();

                    // Ranked without the budget, then allocated here rather than in the store. The
                    // rendered result has to say how many matches were *not* loaded, and only the
                    // caller holding both numbers can say it — a store returns the survivors and
                    // the count it started from is gone.
                    var ranked = await memory.RecallAsync(situation, options with { TokenBudget = null });
                    var allocation = RecallBudget.Apply(ranked, options);

                    return FormatRecallResults(allocation.Matches, allocation.Considered);
                },
                paramName: "situation",
                paramDescription:
                    "Describe the current situation or problem to find relevant " +
                    "patterns, skills, and heuristics")
            .AddTool(
                name: "commit_insight",
                description: commitDescription,
                configure: b => b
                    .Param("description", "Natural language description of the pattern, skill, or heuristic to remember")
                    .Param("kind", "Type of empirical knowledge: 'pattern' (observed correlation), 'skill' (procedure), or 'heuristic' (rule of thumb)")
                    .OnExecute(async args =>
                    {
                        var description = args.Get("description");
                        var kind = args.Get("kind");

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
                    }))
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

    /// <summary>
    /// Renders a recalled set, at whatever depth the budget allowed, with the two things a reader
    /// needs and cannot infer: what it may contradict, and what it is not being shown.
    /// </summary>
    internal static string FormatRecallResults(IReadOnlyList<EmpiricalMatch> matches, int considered = -1)
    {
        if (matches.Count == 0)
            return "No relevant experience found in memory.";

        // A budget tags each match with the depth it bought. Untagged means no budget was in force,
        // and recall keeps returning whole entries as it always has.
        var depth = matches[0].Depth ?? RecallDepth.Full;
        var omitted = considered < 0 ? 0 : considered - matches.Count;

        var sb = new StringBuilder();
        sb.Append($"Found {matches.Count} relevant experience(s)");
        if (depth != RecallDepth.Full)
            sb.Append($", shown at {depth} depth to fit the available context");
        sb.AppendLine(".");

        // An omission that does not announce itself reads as completeness — which is the specific
        // way a partial view becomes a confident false belief.
        if (omitted > 0)
            sb.AppendLine($"{omitted} further match(es) were not loaded; ask again more narrowly to see them.");

        sb.AppendLine();
        sb.Append(EmpiricalRecallRenderer.RenderAuthorityNote(
            hasAdvisory: matches.Any(m => !m.Entry.IsVerifiedRecord),
            hasRecords: matches.Any(m => m.Entry.IsVerifiedRecord)));
        sb.AppendLine();

        foreach (var match in matches)
        {
            sb.Append(EmpiricalRecallRenderer.Render(match, match.Depth ?? depth));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static bool TryParseKind(string kind, out EmpiricalKind result) =>
        Enum.TryParse(kind, ignoreCase: true, out result);
}
