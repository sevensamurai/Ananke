using System.Text;
using Ananke.Orchestration.Agents.Context;
using Ananke.Orchestration.Planning;

namespace Ananke.Design;

/// <summary>
/// Renders a plan and its lineage for a person: the tree as it stands, and version by version what
/// changed, what was added and what was dropped.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is not a projection.</b> <c>PlanTreeProjection</c> renders the tree for a <em>model</em>
/// under a token budget, and it omits things to fit — which is correct there and disqualifying here.
/// A reader asking "what happened to this plan" needs completeness and provenance, so nothing here
/// elides, truncates or summarises. The two want opposite things from the same structure, which is
/// why bending one to serve the other would have quietly cost the model its budget or the reader
/// their record.
/// </para>
/// <para>
/// <b>Everything is derived from the versions themselves.</b> What a version changed is computed by
/// comparing it against the one before it rather than read from an annotation, for the same reason a
/// node carries no status field: a stored answer to a question the structure can answer is a second
/// source of truth. <c>PlanVersion.Reason</c> is the exception, and has to be — why a plan stopped
/// being right is the one thing no diff can recover.
/// </para>
/// <para>
/// <b>A reason and a rationale are printed on separate lines, and the rationale is attributed.</b>
/// One was observed at the halt and the other concluded from it afterwards; a reader who cannot tell
/// them apart is reading a document that looks like a record and is partly a guess.
/// </para>
/// </remarks>
public static class PlanReportExporter
{
    /// <summary>
    /// Renders the version in force: the decomposition, each node's status, and every criterion with
    /// the verdict standing against it.
    /// </summary>
    /// <remarks>
    /// A criterion with no verdict is rendered as <c>unchecked</c> rather than left out. That gap —
    /// between what was verified and what merely looks fine — is the thing abstention exists to keep
    /// visible, and a report that dropped the line would undo it.
    /// </remarks>
    public static string ToOutline(this PlanTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var sb = new StringBuilder();
        sb.AppendLine(
            $"Plan {tree.PlanId} — version {tree.Current.Number} of {tree.Lineage.Count}, "
            + $"root {tree.OutcomeOf(tree.Current.RootId)}");
        sb.AppendLine();

        AppendNode(sb, tree, tree.Current.RootId, depth: 0);

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Renders every version: what it re-ruled, why, and which nodes it changed, added and dropped.
    /// </summary>
    /// <remarks>
    /// Dropped work is named, not counted. "Cancelled" records that something stopped and destroys
    /// what it was for; a lineage says which change of plan made it unnecessary, and leaves it fully
    /// readable in the versions before that one — which is also the only way to answer how much
    /// finished work a contradiction invalidated.
    /// </remarks>
    public static string ToLineage(this PlanTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var sb = new StringBuilder();
        sb.AppendLine($"Lineage of {tree.PlanId} — {tree.Lineage.Count} version(s)");

        for (var i = 0; i < tree.Lineage.Count; i++)
        {
            var version = tree.Lineage[i];
            sb.AppendLine();
            sb.AppendLine($"version {version.Number} — minted {version.MintedAt:u}"
                + (version.ReRuledNodeId is { } reruled ? $", re-ruling {reruled}" : ""));

            if (i == 0)
            {
                sb.AppendLine($"  the plan as first written — {version.Nodes.Count} node(s)");
                continue;
            }

            if (version.Reason is { Length: > 0 } reason)
                sb.AppendLine($"  reason: {reason}");

            if (version.Rationale is { } rationale)
                sb.AppendLine($"  {rationale.By} says: {rationale.Text}");

            AppendDifference(sb, tree.Lineage[i - 1], version);
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>The plan as it stands and how it got there, as one document.</summary>
    public static string ToReport(this PlanTree tree) =>
        $"{tree.ToOutline()}\n\n{tree.ToLineage()}";

    private static void AppendNode(StringBuilder sb, PlanTree tree, string nodeId, int depth)
    {
        var node = tree.Node(nodeId);
        var indent = new string(' ', depth * 2);

        // No "never run" flag: the lifecycle says Planned, and a second copy of one fact is a
        // second thing that can disagree with it.
        List<string> flags = [];
        if (tree.IsStale(nodeId))
            flags.Add($"stale: last ran against version {node.LastRead!.PlanVersion}");

        var state = node.State is StepState.Pending
            ? ""
            : $" — {node.State}" + (node.Result is { } result ? $": {result}" : "");

        sb.AppendLine($"{indent}{nodeId} — {tree.LifecycleOf(nodeId)}/{tree.OutcomeOf(nodeId)}"
            + (flags.Count > 0 ? $" ({string.Join("; ", flags)})" : "")
            + state);
        sb.AppendLine($"{indent}  {node.Contract.Goal}");

        foreach (var constraint in node.Contract.Constraints)
            sb.AppendLine($"{indent}  · constraint  {constraint}");

        var latest = node.LatestVerdicts;
        foreach (var criterion in node.Contract.AcceptanceCriteria)
            AppendCriterion(sb, indent, "", criterion, latest, node.Verdicts);

        foreach (var criterion in node.Contract.QualityCriteria)
            AppendCriterion(sb, indent, "rank ", criterion, latest, node.Verdicts);

        if (node.Violation is { } violation)
            sb.AppendLine($"{indent}  · disputed    \"{violation.Criterion}\" — {violation.Reason}");

        // Kept apart from a dispute, because they are different claims: one says the contract is
        // wrong, the other says nothing was decided at all.
        if (node.Failure is { } failure)
            sb.AppendLine($"{indent}  · failed      {failure.Message}");

        foreach (var childId in node.ChildIds)
            AppendNode(sb, tree, childId, depth + 1);
    }

    private static void AppendCriterion(
        StringBuilder sb,
        string indent,
        string kind,
        string criterion,
        IReadOnlyList<CriterionVerdict> latest,
        IReadOnlyList<CriterionVerdict> all)
    {
        var verdict = latest.FirstOrDefault(
            v => string.Equals(v.Criterion, criterion, StringComparison.Ordinal));

        if (verdict is null)
        {
            sb.AppendLine($"{indent}  · {kind}unchecked  {criterion}");
            return;
        }

        // How many times this criterion was decided the other way before the verdict that stands.
        // A criterion that failed, was fixed and now passes reads very differently from one that
        // simply passed, and the difference is evidence that the fix worked.
        var earlier = all.Count(
            v => string.Equals(v.Criterion, criterion, StringComparison.Ordinal) && v.Passed != verdict.Passed);

        sb.AppendLine($"{indent}  · {kind}{(verdict.Passed ? "met      " : "not met  ")}{criterion}"
            + $"  ({verdict.Oracle})"
            + (earlier > 0 ? $" — after {earlier} attempt(s) that did not" : ""));

        // On its own line, because an oracle that reasons owes its grounds and one that computes
        // does not: an exit code explains itself, a judgement does not.
        if (verdict.Basis is { Length: > 0 } basis)
            sb.AppendLine($"{indent}    {basis}");
    }

    private static void AppendDifference(StringBuilder sb, PlanVersion before, PlanVersion after)
    {
        List<string> added = [.. after.Nodes.Keys.Where(id => !before.Nodes.ContainsKey(id)).Order(StringComparer.Ordinal)];
        List<string> dropped = [.. before.Nodes.Keys.Where(id => !after.Nodes.ContainsKey(id)).Order(StringComparer.Ordinal)];

        var changed = 0;
        var carried = 0;
        foreach (var id in after.Nodes.Keys.Order(StringComparer.Ordinal))
        {
            if (!before.Nodes.TryGetValue(id, out var was))
                continue;

            if (was.Contract == after.Nodes[id].Contract)
            {
                carried++;
                continue;
            }

            changed++;
            sb.AppendLine($"  contract changed: {id}");
            AppendContractDifference(sb, was.Contract, after.Nodes[id].Contract);
        }

        foreach (var id in added)
            sb.AppendLine($"  added:   {id} — {after.Nodes[id].Contract.Goal}");

        foreach (var id in dropped)
        {
            sb.AppendLine($"  dropped: {id} — {before.Nodes[id].Contract.Goal}");
            sb.AppendLine($"           not cancelled: still readable in version {before.Number}");
        }

        if (changed == 0 && added.Count == 0 && dropped.Count == 0)
            sb.AppendLine("  nothing changed");

        sb.AppendLine($"  carried over unchanged: {carried} node(s)");
    }

    private static void AppendContractDifference(StringBuilder sb, AgentContract was, AgentContract now)
    {
        if (!string.Equals(was.Goal, now.Goal, StringComparison.Ordinal))
        {
            sb.AppendLine($"    goal was: {was.Goal}");
            sb.AppendLine($"    goal now: {now.Goal}");
        }

        AppendListDifference(sb, "criterion", was.AcceptanceCriteria, now.AcceptanceCriteria);
        AppendListDifference(sb, "rank", was.QualityCriteria, now.QualityCriteria);
        AppendListDifference(sb, "constraint", was.Constraints, now.Constraints);
    }

    private static void AppendListDifference(
        StringBuilder sb, string kind, IReadOnlyList<string> was, IReadOnlyList<string> now)
    {
        foreach (var gone in was.Where(x => !now.Contains(x, StringComparer.Ordinal)))
            sb.AppendLine($"    {kind} dropped: {gone}");

        foreach (var fresh in now.Where(x => !was.Contains(x, StringComparer.Ordinal)))
            sb.AppendLine($"    {kind} added:   {fresh}");
    }
}
