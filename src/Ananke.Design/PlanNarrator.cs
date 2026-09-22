using Ananke.Orchestration.Planning;
using Ananke.Orchestration.Streaming;

namespace Ananke.Design;

/// <summary>Lines a narrator adds only when asked to.</summary>
[Flags]
public enum PlanNarratorLines
{
    /// <summary>The narration a run always gets.</summary>
    None = 0,

    /// <summary>One line per tool call, with its arguments and what came back.</summary>
    ToolCalls = 1,

    /// <summary>One line per check that passed, which the narration is otherwise silent about.</summary>
    PassedChecks = 2
}

/// <summary>
/// Turns a supervised plan's events into lines a person can read, one event at a time.
/// </summary>
/// <remarks>
/// <para>
/// The events are typed because each carries something the others do not — what a node was shown,
/// what nothing could decide, what a change of plan dropped — and collapsing them into one type
/// would make every payload nullable. What should <em>not</em> follow from that is every consumer
/// writing the same forty-line switch to print them, so this ships the switch once.
/// </para>
/// <para>
/// <b>It is stateful, and that is the part worth having.</b> A node's step and what it reported are
/// two events and read better as one line, so a started node is held until something else arrives —
/// which also means a node that never reports still gets its line. Step and pass numbers are counted
/// here for the same reason: they are facts about the narration, not about the plan, and the tree
/// deliberately holds neither.
/// </para>
/// <para>
/// One narrator per run. <see cref="Describe"/> returns <see langword="null"/> when an event has
/// nothing to add — anything that is not a plan event, and a ruling that simply passed.
/// </para>
/// <para>
/// <b><paramref name="width"/> is the one thing a caller tunes, and it is why there is no second
/// renderer.</b> A console wants the shape of the run and a log wants every word a model wrote;
/// those are the same events at two widths, not two formats. Pass <see cref="int.MaxValue"/> for a
/// transcript that clips nothing.
/// </para>
/// </remarks>
/// <param name="width">
/// How much of any one model-written sentence a line may carry. The default suits a terminal.
/// </param>
/// <param name="lines">
/// Lines that are off by default: a run that narrated every tool call and every gate it cleared would
/// bury the events worth reading. A log kept beside the console usually asks for both.
/// </param>
public sealed class PlanNarrator(int width = 150, PlanNarratorLines lines = PlanNarratorLines.None)
{
    private PlanNodeStarted? _running;
    private int _steps;
    private int _passes;

    /// <summary>
    /// The line, or lines, this event adds to the narration — or <see langword="null"/> for an event
    /// with nothing to say.
    /// </summary>
    public string? Describe(WorkflowEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        switch (evt)
        {
            case PlanNodeStarted started:
                var previous = Flush();
                _running = started;
                return previous;

            case PlanNodeReported reported:
                return Flush(reported.Summary);

            case PlanNodeDisputed disputed:
                return Join(Flush(), $"      ⚠ disputes its contract: \"{disputed.Criterion}\"");

            // Its own line, because no version is minted for it. Without one the only trace of a
            // decision somebody actually took would be a field on a node nobody thought to read.
            // Its own line, and it says the run is fine: the plan is right, and a decision inside
            // the work is waiting for somebody. Read as a failure it would be the opposite of true.
            case PlanNodeBlocked blocked:
                return Join(Flush(), $"      ⏳ waiting to be told: {Short(blocked.Asks)}");

            case PlanNodeAnswered answered:
                return $"   → '{answered.NodeId}' was told: {answered.Answer} ({answered.By})";

            case PlanStepAbandoned abandoned:
                return Join(Flush(), $"   ⊗ '{abandoned.NodeId}' given up by {abandoned.By}: "
                    + $"{abandoned.Reason}");

            // Its own line, and it says the run continues. A failure that read like a crash would
            // put back, for a reader, the thing the tier stopped doing: ending a run over one step.
            case PlanNodeFailed failed:
                return Join(Flush(), $"      ✗ the attempt failed: {failed.Message}");

            // Said once. When the outcome *is* the abstention, naming it and then naming what it
            // was about says the same thing twice.
            case PlanNodeVerified { Abstained.Count: > 0 } ruled:
                return Join(Flush(), ruled.Outcome is VerificationOutcome.Abstained
                    ? $"      → nothing could decide \"{ruled.Abstained[0]}\""
                    : $"      → {Ruled(ruled.Outcome)}, and nothing could decide "
                      + $"\"{ruled.Abstained[0]}\"");

            case PlanNodeVerified { Outcome: not VerificationOutcome.Passed } ruled:
                return Join(Flush(), $"      → {Ruled(ruled.Outcome)}");

            case PlanPassCompleted done:
                var skipped = done.Skipped.Count == 0
                    ? ""
                    : $", {done.Skipped.Count} skipped as already done";

                var halted = done.HaltedAt is null ? "" : $", halted at {done.HaltedAt} (version {done.PlanVersion})";
                return Join(Flush(),
                    $"     ── pass {++_passes}: {done.Executed.Count} step(s) ran{skipped}{halted}\n");

            // The tier's central act, and the only one that used to leave no line unless it happened
            // to mint a version. What it was looking at, what it chose, and what that leaves.
            case PlanDecisionTaken decided:
                // The budget clause describes a bound only a round the run took by itself is under.
                // An escalated answer is reported and never counted, so printing a count beside one
                // would narrate a cap that is not in force — and that somebody answered is the more
                // useful fact anyway.
                var under = (decided.Escalated, decided.MaxChanges) switch
                {
                    (true, _) => " — answered by a person",
                    (false, { } ceiling) => $" — {decided.Changes} of {ceiling} changes of plan used",
                    _ => $" — changes of plan so far: {decided.Changes}"
                };

                // One line, because it is one event. The decision is what came back, and who answered
                // is the part an audit wants — why it was asked is on the evidence the supervisor was
                // shown, not on this event, so it is not restated here.
                return Join(Flush(),
                    $"     ⟐ '{decided.NodeId}' halted → {Decided(decided)}{under} (version {decided.PlanVersion})");

            case PlanVersionMinted minted:
                var minting = Join(Flush(),
                    $"   the plan changed — version {minted.PlanVersion}, new instructions for '{minted.ReRuledNodeId}'",
                    $"     reason: {Short(minted.Reason)}");

                // On its own line, and named for who said it. What was observed and what somebody
                // concluded from it read identically once they share a label.
                //
                // Clipped for the same reason a step's report is: both are model-written, and a
                // paragraph here hides the version it is explaining.
                if (minted.Rationale is { } rationale)
                    minting = Join(minting, $"     {rationale.By} says: {Short(rationale.Text)}");

                // Named, never counted. Which work a change of plan dropped is the question a
                // "cancelled" flag cannot answer afterwards.
                foreach (var goneId in minted.DroppedNodeIds)
                    minting = Join(minting, $"     no longer part of the plan: {goneId}");

                // Counted *and* named. The count is the finding — a plan can leave a change of plan
                // gated by less than it went in with — and the words are what makes it actionable,
                // since a criterion nothing can decide is usually one nobody has written a check for
                // yet. Silence here would be the failure itself: a redesign that quietly stops being
                // checked reads exactly like one that passed.
                if (minted.CriteriaNothingCanDecide is { Count: > 0 } undecidable)
                {
                    minting = Join(minting, $"     criteria nothing can decide: {undecidable.Count}");
                    foreach (var criterion in undecidable)
                        minting = Join(minting, $"       \"{criterion}\"");
                }

                return minting;

            case PlanProposalOffered offered:
                var offer = Join(Flush(),
                    $"   supervisor asked about '{offered.NodeId}' (version {offered.PlanVersion}): "
                    + Short(offered.HaltReason ?? "nothing recorded why"));

                foreach (var refused in offered.Discarded)
                    offer = Join(offer, $"     refused: {Short(refused)}");

                foreach (var option in offered.Options)
                    offer = Join(offer, $"     {(option.Recommended ? "→" : " ")} {option.Summary}"
                        + (option.Rationale is { } reading ? $" ({Short(reading.Text)})" : ""));

                return offered.Options.Count > 0
                    ? offer
                    : Join(offer, offered.Exhausted
                        ? "     ran out of tool rounds before offering anything"
                        : "     nothing to offer");

            case PlanReauthored reauthored:
                return Join(Flush(), "   planner wrote: " + string.Join(" · ", reauthored.Authored.Select(
                    step => $"{step.Id} {string.Join(", ", step.Contract.AcceptanceCriteria)}")));

            case PlanNotReauthored declined:
                return Join(Flush(), $"   planner wrote nothing: {declined.Reason}");

            // Asked for, because a reader watching a check pass wants to see it happen — and because
            // the events a log keeps are not the events a console has room for.
            case PlanNodeVerified { Outcome: VerificationOutcome.Passed } passed
                when lines.HasFlag(PlanNarratorLines.PassedChecks):
                return Join(Flush(), $"      ✓ {passed.NodeId}: {Ruled(passed.Outcome)}");

            // Not flushed: a tool call happens while a node is running, so the node's own line still
            // belongs after the calls it made.
            case AgentToolCalled called when lines.HasFlag(PlanNarratorLines.ToolCalls):
                var result = called.ResultLength > called.Result.Length
                    ? $"{called.Result} … ({called.ResultLength} characters)"
                    : called.Result;

                return $"      tool {called.AgentName}: {called.ToolName} {called.Arguments} → "
                    + (called.IsError ? $"error: {result}" : result);

            default:
                // Including a ruling that passed: a run that narrated every gate it cleared would
                // bury the four events worth reading in a wall of the ones that are not.
                return null;
        }
    }

    /// <summary>How much of a step's own account belongs in a line of narration.</summary>
    /// <remarks>
    /// <b>A narration is for reading.</b> A model asked what it did will happily write a paragraph,
    /// and three of those in a row hide the shape of the run they are describing. Nothing is lost:
    /// the full account is on <c>PlanNodeReported</c>, where anything that wants it can read it —
    /// and a narrator built at full width prints it whole.
    /// </remarks>
    private string Short(string summary) =>
        summary.Length <= width ? summary : summary[..(width - 1)].TrimEnd() + "…";

    /// <summary>What the checks made of a step, in a reader's words rather than an enum's.</summary>
    private static string Ruled(VerificationOutcome outcome) => outcome switch
    {
        VerificationOutcome.Passed => "the checks agree it is done",
        VerificationOutcome.GateFailed => "a check says no",
        VerificationOutcome.Abstained => "nothing could decide it",
        _ => "the checks could not settle its dispute"
    };

    /// <summary>What was decided, and what it means for the work.</summary>
    private static string Decided(PlanDecisionTaken decided) => decided.Decision switch
    {
        PlanDecision.ReplanPlan { Contract: null } => "replan — the Planner rewrites the plan",

        PlanDecision.ReplanPlan replan =>
            $"change what '{replan.NodeId ?? decided.NodeId}' is asked to do"
            + (replan.Children is { Count: > 0 } children
                ? $"; the work below it becomes {children.Count} step(s)"
                : ""),

        PlanDecision.AskPlan { Options.Count: > 0 } asking =>
            $"ask — {asking.Options.Count} option(s) offered",

        PlanDecision.AskPlan => "ask — nothing to offer",

        _ => "stop here"
    };

    /// <summary>The held step's line, if a node is waiting on one.</summary>
    private string? Flush(string summary = "")
    {
        if (_running is not { } started)
            return null;

        _running = null;
        var line = $"  {++_steps,2}. {started.NodeId,-24} {Short(summary)}";

        return started.SawEverything
            ? line
            : Join(line, $"      (read {started.ProjectedTokens} tokens of the plan; "
                + $"{started.OmittedRecords} record(s) not shown)");
    }

    private static string Join(string? first, params string[] rest) =>
        first is null ? string.Join('\n', rest) : string.Join('\n', [first, .. rest]);
}

/// <summary>Reading a supervised run as text.</summary>
public static class PlanNarrationExtensions
{
    /// <summary>
    /// Narrates a workflow's events as they arrive, yielding one entry per event that has something
    /// to say.
    /// </summary>
    /// <example>
    /// <code>
    /// await foreach (var line in workflow.StreamAsync(state).Narrate())
    ///     Console.WriteLine(line);
    /// </code>
    /// </example>
    public static async IAsyncEnumerable<string> Narrate(
        this IAsyncEnumerable<WorkflowEvent> events,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(events);

        var narrator = new PlanNarrator();

        await foreach (var evt in events.WithCancellation(ct).ConfigureAwait(false))
        {
            if (narrator.Describe(evt) is { } line)
                yield return line;
        }
    }
}
