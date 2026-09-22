namespace Ananke.Orchestration.Planning;

/// <summary>
/// What a seat is told about a halt: what somebody said, what earlier runs settled, what the plan
/// already tried, and what was refused.
/// </summary>
/// <remarks>
/// <para>
/// <b>The same record, whichever seat reads it.</b> A supervisor asked for options and a Planner asked
/// to rewrite the plan are looking at one halt, and a section written for one of them is the same
/// section for the other. Kept here so a second seat reaches it without owning the first.
/// </para>
/// <para>
/// Each section is empty when there is nothing to say, so a record composes by addition and a halt
/// nobody answered reads without a heading for it.
/// </para>
/// </remarks>
public static class PlanHaltRecord
{
    /// <summary>
    /// The whole plan at a halt, for the seat that decides which steps there should be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The plan, not the step.</b> A supervisor is asked what to do about the step that stopped and
    /// is shown that step; this seat is asked which steps there should be, so it is shown every one of
    /// them with where it stands and what it settled on, what binds the plan, why it stopped, and what
    /// has already been tried.
    /// </para>
    /// <para>
    /// Giving a step up is not offered here. That is an option about this step, and the seat that
    /// weighs it is the one that can see the halt.
    /// </para>
    /// </remarks>
    /// <param name="coordination">The halt.</param>
    /// <param name="refused">What this seat already wrote that could not be used.</param>
    public static string ForPlanner(PlanCoordination coordination, IReadOnlyList<string> refused)
    {
        ArgumentNullException.ThrowIfNull(coordination);
        ArgumentNullException.ThrowIfNull(refused);

        var tree = coordination.Result.Tree;

        return $"""
            {PlanTreeProjection.Project(tree, tree.Root.Id).Text}
            {Binding(tree)}
            Why it stopped: {coordination.HaltReason}


            """
            + Said(coordination)
            + Remembered(coordination)
            + Tried(coordination)
            + Refused(refused);
    }

    /// <summary>What the plan has settled that still binds, and nothing when nothing does.</summary>
    /// <remarks>
    /// A term binds while nothing has contradicted it, which is the test the plan itself applies when
    /// it binds terms into the contracts it authors. Why a term ended says which kind of ending it was,
    /// not whether it ended.
    /// </remarks>
    private static string Binding(PlanTree tree)
    {
        var terms = tree.Terms.Where(term => term.InForce).ToList();

        return terms.Count == 0
            ? string.Empty
            : $"""
               ## Settled about this plan, and binding
               {Bulleted(terms.Select(t => $"{t.Reading} — from {t.By}, who said: \"{t.Said}\""))}

               """;
    }

    /// <summary>
    /// What somebody said when they answered off the list, and nothing when nobody has.
    /// </summary>
    /// <remarks>
    /// <b>Verbatim, and marked as theirs.</b> It is the one input here nobody in the loop authored, so
    /// it is worth more than anything this seat inferred last round — and quoting it rather than
    /// summarising it is what lets the reading be checked against the source afterwards.
    /// </remarks>
    public static string Said(PlanCoordination coordination)
    {
        ArgumentNullException.ThrowIfNull(coordination);

        return string.IsNullOrWhiteSpace(coordination.Said)
            ? string.Empty
            : $"""
               You asked, and the answer was none of the options you offered. In their own words:

                 "{coordination.Said!.Trim()}"

               That is the most recent thing anybody has told you and it outranks what you proposed
               last round. Work out what it means for this halt and offer options that take it as
               settled — do not offer again what they have just declined, and do not ask them to
               choose between your old options a second time.


               """;
    }

    /// <summary>
    /// What earlier runs settled that might apply here, and nothing when nothing does.
    /// </summary>
    /// <remarks>
    /// <b>Marked as remembered rather than stated, and the marking is the safety property.</b> A
    /// preference somebody expressed last week, about a plan that may no longer resemble this one,
    /// must never read to a supervisor — or to anybody auditing afterwards — as though it had been
    /// said here. It binds nothing on its own; adopting one is a decision this run takes.
    /// </remarks>
    public static string Remembered(PlanCoordination coordination)
    {
        ArgumentNullException.ThrowIfNull(coordination);

        return coordination.Recalled is not { Count: > 0 } recalled
            ? string.Empty
            : $"""
               Remembered from earlier runs, and <b>not</b> said by anybody here:
               {Bulleted(recalled.Select(t => $"\"{t.Reading}\" — from {t.By}, who said: \"{t.Said}\""))}

               These bind nothing. Treat them as things somebody once wanted, which may or may not
               still apply to this plan: if one of them clearly does, report it as a term and it will
               be adopted for this run. If it does not, ignore it — repeating a preference nobody here
               has expressed narrows a plan on nobody's authority.


               """;
    }

    /// <summary>What earlier versions of the plan asked for, and nothing when it has not changed.</summary>
    public static string Tried(PlanCoordination coordination)
    {
        ArgumentNullException.ThrowIfNull(coordination);

        return PlanTreeProjection.Lineage(coordination.Result.Tree) is { Length: > 0 } lineage
            ? lineage + "\nIf the plan keeps stopping on the same finding, say so in the replan's rationale.\n\n\n"
            : string.Empty;
    }

    /// <summary>What was refused last time, and nothing when nothing was.</summary>
    /// <remarks>
    /// <b>The correction, not just the rejection.</b> Every guard states the shape it would have
    /// accepted; showing that is the difference between asking again and asking again differently.
    /// </remarks>
    public static string Refused(IReadOnlyList<string> refused)
    {
        ArgumentNullException.ThrowIfNull(refused);

        return refused.Count == 0
            ? string.Empty
            : $"""
               You have already answered this once and none of it could be used. What was wrong,
               and what would have been accepted:
               {Bulleted(refused)}

               Do not offer any of those again. If what you meant is right and only the shape was
               wrong, say the same thing in the shape that would be accepted.


               """;
    }

    /// <summary>One line per item, indented for a prompt.</summary>
    private static string Bulleted(IEnumerable<string> lines) =>
        string.Join(Environment.NewLine, lines.Select(line => $"  - {line}"));
}
