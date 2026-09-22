namespace Ananke.Orchestration.Planning;

/// <summary>One reason a plan was not admitted.</summary>
public sealed record PlanFault
{
    /// <summary>The step it is about.</summary>
    public required string NodeId { get; init; }

    /// <summary>What is wrong, in a sentence somebody can act on.</summary>
    public required string Problem { get; init; }

    public override string ToString() => $"{NodeId}: {Problem}";
}

/// <summary>
/// What a plan has to be before it is allowed to run.
/// </summary>
/// <remarks>
/// <para>
/// <b>A program, deliberately.</b> Everything here is cheap to detect and expensive to discover: a
/// criterion nothing can rule on abstains at pass three, having spent the budget getting there, and
/// a step that is its own ancestor killed the process with an uncatchable stack overflow. None of it
/// needs judgement, so none of it should reach a model.
/// </para>
/// <para>
/// <b>It matters most for a plan nobody wrote by hand.</b> While plans came out of a manifest their
/// author had read, admission was a formality; a plan authored by a model on every run, and re-
/// authored at every halt, is a plan whose shape nobody has checked.
/// </para>
/// <para>
/// <b>What it does not do is judge the plan.</b> Whether these are the right steps is the question
/// the whole tier exists to ask. This answers only whether the plan is one.
/// </para>
/// </remarks>
public static class PlanAdmission
{
    /// <summary>
    /// Every reason <paramref name="tree"/> may not run, or empty if there is none.
    /// </summary>
    /// <param name="tree">The plan.</param>
    /// <param name="checks">
    /// What is available to rule on it. Empty admits any criterion, which is what a plan with no
    /// deterministic checks has always done.
    /// </param>
    /// <param name="judged">
    /// Criteria that are deliberately nobody's to decide by running something. They are admitted and
    /// they are <em>counted</em>, so "how much of this plan rests on an opinion" is answerable before
    /// anything is spent rather than discovered as abstention afterwards.
    /// </param>
    public static IReadOnlyList<PlanFault> Faults(
        PlanTree tree,
        IReadOnlyList<IDeterministicCheck>? checks = null,
        IReadOnlyCollection<string>? judged = null)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var faults = new List<PlanFault>();
        var version = tree.Current;

        foreach (var (id, node) in version.Nodes)
        {
            // A step that is its own ancestor is not a plan, and finding out by running it means
            // finding out from a stack overflow that no catch block sees.
            if (Ancestry(version, id) is { } cycle)
                faults.Add(new PlanFault { NodeId = id, Problem = cycle });

            if (node.ParentId is { } parent && !version.Nodes.ContainsKey(parent))
                faults.Add(new PlanFault
                {
                    NodeId = id,
                    Problem = $"its parent '{parent}' is not in the plan"
                });

            if (string.IsNullOrWhiteSpace(node.Contract.Goal))
                faults.Add(new PlanFault { NodeId = id, Problem = "it has no goal" });

            if (checks is not { Count: > 0 })
                continue;

            foreach (var criterion in node.Contract.AcceptanceCriteria)
            {
                if (judged?.Contains(criterion) == true)
                    continue;

                if (checks.Any(check => check.CanRule(criterion)))
                {
                    // Resolvable, and possibly still nonsense: the right check asked about something
                    // that does not exist answers no for ever, and the step disputes a contract that
                    // was impossible when it was written.
                    if (Nonsense(checks, criterion) is { } why)
                        faults.Add(new PlanFault
                        {
                            NodeId = id,
                            Problem = $"\"{criterion}\" could never hold: {why}"
                        });

                    continue;
                }

                faults.Add(new PlanFault
                {
                    NodeId = id,
                    Problem = $"nothing can decide \"{criterion}\" — it names no check that exists, "
                        + "so this step could never be shown to have been done"
                });
            }
        }

        return faults;
    }

    /// <summary>Whether the plan may run.</summary>
    public static bool Admits(
        PlanTree tree,
        IReadOnlyList<IDeterministicCheck>? checks = null,
        IReadOnlyCollection<string>? judged = null) =>
        Faults(tree, checks, judged).Count == 0;

    /// <summary>
    /// How many of the plan's acceptance criteria nothing can run, by step.
    /// </summary>
    /// <remarks>
    /// Not a fault: a plan may legitimately hold criteria only judgement can settle. It is a number
    /// worth having <em>before</em> the run, because it says how much of the plan is opinion.
    /// </remarks>
    public static IReadOnlyDictionary<string, int> Judged(
        PlanTree tree, IReadOnlyList<IDeterministicCheck>? checks = null)
    {
        ArgumentNullException.ThrowIfNull(tree);

        return tree.Current.Nodes
            .Select(entry => (
                entry.Key,
                Count: entry.Value.Contract.AcceptanceCriteria.Count(
                    criterion => checks is not { Count: > 0 }
                        || !checks.Any(check => check.CanRule(criterion)))))
            .Where(entry => entry.Count > 0)
            .ToDictionary(entry => entry.Key, entry => entry.Count, StringComparer.Ordinal);
    }

    /// <summary>Why a resolvable criterion is not answerable, or <see langword="null"/> if it is.</summary>
    public static string? Nonsense(IReadOnlyList<IDeterministicCheck> checks, string criterion) =>
        checks.OfType<InvocationCheck>()
            .Select(check => check.Nonsense(criterion))
            .FirstOrDefault(why => why is not null);

    private static string? Ancestry(PlanVersion version, string id)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var at = id; at is not null;)
        {
            if (!seen.Add(at))
                return $"it is its own ancestor, through '{at}'";

            at = version.Nodes.TryGetValue(at, out var node) ? node.ParentId : null;
        }

        return null;
    }
}
