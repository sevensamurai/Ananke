using System.Text;
using Ananke.Orchestration.Agents.Context;

namespace Ananke.Orchestration.Planning;

/// <summary>
/// Renders what one node should read of the tree it sits in, at whatever depth its allocation
/// allows.
/// </summary>
/// <remarks>
/// <para>
/// This is the read half of "nothing crosses a node boundary". A node is not handed its siblings'
/// results; it is handed a view of the durable tree, and what it did not read is still there to be
/// read again. That is the whole difference between a loss that is recoverable and one that is not.
/// </para>
/// <para>
/// <b>What it gives ground on, in order.</b> The node's own contract is pinned and never trimmed —
/// it is the thing the node is for. Everything else is the tree seen from a distance, and it
/// compresses the way distance compresses: established verdicts trim to the most recent first, then
/// ancestors lose their criteria and keep their goals, and only then are the most distant ancestors
/// dropped altogether. Whatever is left out is <b>stated</b>, because an omission that does not
/// announce itself reads as completeness.
/// </para>
/// </remarks>
public static class PlanTreeProjection
{
    /// <summary>What one projection decided.</summary>
    /// <param name="Text">The rendered view, ready to hand to a node.</param>
    /// <param name="EstimatedTokens">What it is estimated to cost.</param>
    /// <param name="OmittedAncestors">How many ancestors were not described at all.</param>
    /// <param name="OmittedVerdicts">How many established verdicts were not listed.</param>
    public readonly record struct Projection(
        string Text,
        int EstimatedTokens,
        int OmittedAncestors,
        int OmittedVerdicts);

    /// <summary>Projects the tree for <paramref name="nodeId"/> under an optional token budget.</summary>
    /// <param name="tree">The plan.</param>
    /// <param name="nodeId">The node about to run.</param>
    /// <param name="tokenBudget">
    /// What the surrounding view may occupy. <see langword="null"/> renders everything — the right
    /// default for a small plan, where trimming would only lose information for nothing.
    /// </param>
    public static Projection Project(PlanTree tree, string nodeId, int? tokenBudget = null)
    {
        ArgumentNullException.ThrowIfNull(tree);

        var ancestors = tree.Ancestors(nodeId);
        var established = Established(tree, nodeId);

        // Render the fullest form first; give ground only if it does not fit.
        for (var ancestorDetail = true; ; ancestorDetail = false)
        {
            for (var keptAncestors = ancestors.Count; keptAncestors >= 0; keptAncestors--)
            {
                for (var keptVerdicts = established.Count; keptVerdicts >= 0; keptVerdicts--)
                {
                    var text = Render(
                        tree, nodeId, ancestors, established, ancestorDetail, keptAncestors, keptVerdicts);
                    var cost = ApproximateTokenCounter.Instance.EstimateTokens(text);

                    if (tokenBudget is not > 0 || cost <= tokenBudget)
                    {
                        return new Projection(
                            text, cost,
                            ancestors.Count - keptAncestors,
                            established.Count - keptVerdicts);
                    }
                }
            }

            if (!ancestorDetail)
                break;
        }

        // Everything trimmed and still over budget: the node's own contract alone, which is the one
        // thing that is never given up.
        var minimal = Render(tree, nodeId, ancestors, established, false, 0, 0);
        return new Projection(
            minimal,
            ApproximateTokenCounter.Instance.EstimateTokens(minimal),
            ancestors.Count,
            established.Count);
    }

    /// <summary>
    /// What the plan asked for before its current version, and why each version was replaced. Empty for
    /// a plan that has not changed.
    /// </summary>
    public static string Lineage(PlanTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        if (tree.Lineage.Count < 2)
            return string.Empty;

        var text = new StringBuilder("## What this plan already tried, oldest first\n");

        for (var i = 0; i < tree.Lineage.Count - 1; i++)
        {
            var version = tree.Lineage[i];
            var steps = version.Nodes.Values
                .Where(node => node.Id != version.RootId)
                .Select(node => $"{node.Id} [{string.Join("; ", node.Contract.AcceptanceCriteria)}]");

            text.Append("- version ").Append(version.Number).Append(": ")
                .Append(string.Join(", ", steps))
                .Append(" — replaced because: ")
                .AppendLine(tree.Lineage[i + 1].Reason ?? "nothing recorded why");
        }

        return text.ToString();
    }

    /// <summary>
    /// Verdicts and reported contradictions recorded anywhere else in the plan, most recent first.
    /// </summary>
    /// <remarks>
    /// Deliberately not restricted to siblings. A node that reads only its immediate neighbours has
    /// reinvented the handoff with extra steps; the point is that the whole structure is readable.
    /// </remarks>
    private static IReadOnlyList<(string NodeId, string Line, DateTimeOffset At)> Established(
        PlanTree tree, string nodeId)
    {
        var lines = new List<(string, string, DateTimeOffset)>();

        foreach (var node in tree.Current.Nodes.Values)
        {
            if (node.Id == nodeId)
                continue;

            foreach (var verdict in node.Verdicts)
            {
                lines.Add((node.Id,
                    $"{(verdict.Passed ? "met" : "NOT met")}: {verdict.Criterion} "
                    + $"— {verdict.Oracle}, {verdict.At:yyyy-MM-dd HH:mm} UTC",
                    verdict.At));
            }

            if (node.Violation is { } violation)
            {
                lines.Add((node.Id,
                    $"contract disputed: {violation.Criterion} — {violation.Reason}",
                    violation.At));
            }
        }

        return [.. lines.OrderByDescending(l => l.Item3)];
    }

    private static string Render(
        PlanTree tree,
        string nodeId,
        IReadOnlyList<PlanNode> ancestors,
        IReadOnlyList<(string NodeId, string Line, DateTimeOffset At)> established,
        bool ancestorDetail,
        int keptAncestors,
        int keptVerdicts)
    {
        var node = tree.Node(nodeId);
        var text = new StringBuilder("# Plan context\n\n");

        // Nearest ancestor last, so the chain reads top-down into the node's own contract.
        //
        // The omission notices below sit *outside* the "did anything survive" checks on purpose. A
        // section trimmed to nothing is the case where silence is most misleading — the node would
        // read a view with no sign that a plan surrounds it — and it is exactly the case an
        // `if (kept > 0)` guard would skip.
        if (ancestors.Count > 0)
        {
            text.AppendLine("## Where this sits");
            foreach (var ancestor in ancestors.Take(keptAncestors).Reverse())
            {
                text.Append("- ").Append(ancestor.Contract.Goal);
                if (ancestorDetail && ancestor.Contract.AcceptanceCriteria.Count > 0)
                {
                    text.Append(" — must satisfy: ")
                        .Append(string.Join("; ", ancestor.Contract.AcceptanceCriteria));
                }

                if (ancestor.Contract.Constraints.Count > 0)
                    text.Append(" — holds: ").Append(string.Join("; ", ancestor.Contract.Constraints));

                text.AppendLine();
            }

            if (keptAncestors < ancestors.Count)
            {
                text.Append("- (").Append(ancestors.Count - keptAncestors)
                    .AppendLine(" further level(s) above are not shown here; read the plan for them.)");
            }

            text.AppendLine();
        }

        if (established.Count > 0)
        {
            text.AppendLine("## Already established elsewhere in this plan");
            foreach (var (owner, line, _) in established.Take(keptVerdicts))
                text.Append("- [").Append(owner).Append("] ").AppendLine(line);

            if (keptVerdicts < established.Count)
            {
                text.Append("- (").Append(established.Count - keptVerdicts)
                    .AppendLine(" further record(s) not shown; the plan still holds them.)");
            }

            text.AppendLine();
        }

        // Every step under the same parent, in order, with where it stands and what it settled on, so
        // a step can fit around the others. A node with steps of its own lists those.
        if ((node.ParentId ?? (node.ChildIds.Count > 0 ? node.Id : null)) is { } parentId)
        {
            text.AppendLine("## Steps in this plan, in order");

            foreach (var step in tree.Node(parentId).ChildIds.Select(tree.Node))
            {
                text.Append("- ").Append(step.Id).Append(": ").Append(step.Contract.Goal);

                if (step.Contract.AcceptanceCriteria.Count > 0)
                    text.Append(" [").Append(string.Join(", ", step.Contract.AcceptanceCriteria)).Append(']');

                text.Append(" — ").AppendLine(
                    step.Id == nodeId ? "this step"
                    : step.Result is { } result ? $"{step.State}: {result}"
                    : step.State.ToString());
            }

            text.AppendLine();
        }

        text.AppendLine("## This node");
        text.AppendLine(node.Contract.Render());

        return text.ToString();
    }
}
