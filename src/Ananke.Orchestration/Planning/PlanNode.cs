using Ananke.Orchestration.Agents.Context;

namespace Ananke.Orchestration.Planning;

/// <summary>
/// One work item in a decomposition: the contract it was given, the children it decomposed into,
/// and the verdicts recorded against its criteria.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="State"/> and <see cref="Result"/> are marked by the loop, from what the step
/// reported</b> — never authored by the node itself. The reason behind them stays where it was
/// found: <see cref="LastRead"/>'s summary, <see cref="Question"/>, or the verdicts. When the plan
/// itself has to change, that is a new plan version with a stated reason, not a flag flipping.
/// <see cref="AttemptStartedAt"/> is the other fact stored rather than derived, and the reason is
/// with it.
/// </para>
/// <para>
/// It also holds no message history. Nothing is handed from one node to the next — what a node
/// knows, it reads from the tree.
/// </para>
/// </remarks>
public sealed record PlanNode
{
    /// <summary>Identifier, unique within the plan.</summary>
    public required string Id { get; init; }

    /// <summary>The parent that authored this node's contract, or <see langword="null"/> at the root.</summary>
    public string? ParentId { get; init; }

    /// <summary>What this node was asked to do, and what would count as having done it.</summary>
    public required AgentContract Contract { get; init; }

    /// <summary>Children, in execution order. Execution is sequential, so the order is meaningful.</summary>
    public IReadOnlyList<string> ChildIds { get; init; } = [];

    /// <summary>Verdicts recorded against this node's acceptance criteria.</summary>
    public IReadOnlyList<CriterionVerdict> Verdicts { get; init; } = [];

    /// <summary>
    /// A contradiction this node reported between its contract and what it found, if it reported one.
    /// </summary>
    /// <remarks>
    /// A peer of a result, not an error. A node that discovers its contract is wrong returns that,
    /// and it travels to whoever authored the criterion. The failure mode is not that the
    /// contradiction happens — a leaf contradicting a decision made at the root is the cheapest
    /// place that discovery will ever happen — it is that the contradiction has nowhere to go, so
    /// the node either grinds against a contract it knows is wrong or quietly routes around it.
    /// </remarks>
    public PlanViolation? Violation { get; init; }

    /// <summary>
    /// Somebody's decision that this work is given up, if one was taken.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Stored, for the same reason <see cref="Violation"/> is stored.</b> A dispute is a report
    /// nothing else witnessed; an abandonment is a decision nothing else took. Neither can be derived
    /// from verdicts, and both carry the reason that a bare status word would have destroyed — which is
    /// what the rule against status fields is actually about.
    /// </para>
    /// <para>
    /// <b>Giving one thing up is not re-planning.</b> Before this, the only way to say it was to
    /// re-author the parent's whole child list, which deletes by omission: asked to drop one highlight,
    /// a live run re-ruled the root and destroyed six finished steps that were never mentioned. The work
    /// stays where it is now, and the record on it says who gave it up and why.
    /// </para>
    /// </remarks>
    public PlanAbandonment? Abandonment { get; init; }

    /// <summary>
    /// What this node is waiting to be told, if it is waiting on anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not a dispute, and the difference is the whole of it.</b> A dispute says the contract is
    /// wrong and stands until the contract changes. This says the contract is <em>fine</em> and the
    /// work has reached a choice it is not entitled to make — several admissible answers, and nothing
    /// in the step's remit to pick between them. Encoded as a dispute it would mint a version for a
    /// plan that was never defective and leave the record permanently claiming otherwise.
    /// </para>
    /// <para>
    /// <b>The step supplies the options because the step found them.</b> It is the thing that queried
    /// the facts; what it cannot do is weigh them. Cleared when an answer arrives.
    /// </para>
    /// </remarks>
    public NodeQuestion? Question { get; init; }

    /// <summary>Where this step stands, as the loop marked it from what the step reported.</summary>
    /// <remarks>
    /// <see cref="StepState.Pending"/> for a step that has not reported, or reported nothing the loop
    /// can mark; its verdicts are then its only record.
    /// </remarks>
    public StepState State { get; init; }

    /// <summary>The option this step settled on, when it is <see cref="StepState.Done"/>.</summary>
    public string? Result { get; init; }

    /// <summary>What this node has been told, most recent last.</summary>
    /// <remarks>
    /// <b>Accumulates, like verdicts.</b> An answer is a fact about the work — somebody decided this,
    /// and here is who — and a node asked twice has two of them. It reaches the next attempt through
    /// the node itself, which is the only channel there is: nothing is handed from one run to the next.
    /// </remarks>
    public IReadOnlyList<PlanAnswer> Answers { get; init; } = [];

    /// <summary>
    /// An attempt at this node that did not finish — retries exhausted, a tool fault, a reply that
    /// would not parse. <see langword="null"/> when the last attempt ran to completion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not a verdict, and never written as one.</b> A verdict claims something was checked, and a
    /// node that died checked nothing. Recording a failing verdict instead would put a claim in the
    /// tree that nobody made — the same refusal that keeps a node from inventing a passing one.
    /// </para>
    /// <para>
    /// <b>About an attempt, not about the contract</b> — which is what separates it from
    /// <see cref="Violation"/>. A dispute is a claim that the contract is wrong, and it stands until
    /// the contract changes; a failure is a claim about one run, and the next run supersedes it. That
    /// is why a retry can resolve a failure and cannot resolve a dispute.
    /// </para>
    /// </remarks>
    public NodeFailure? Failure { get; init; }

    /// <summary>
    /// When the attempt now in flight began, or <see langword="null"/> when none is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The one thing about a node that is stored rather than derived</b>, and it is not an
    /// exception to the rule above. That rule refuses a status <em>flag</em>, because a word like
    /// "complete" records an outcome and destroys the reason behind it. <em>In flight since T</em> is
    /// neither an outcome nor a judgement — it is a fact about right now, and it cannot be
    /// reconstructed from verdicts because a node that is running has not produced any yet.
    /// </para>
    /// <para>
    /// <b>What it buys is the question a long pause makes urgent.</b> A plan that stops for days
    /// and is read by somebody who was not there is asked <em>which step is in flight</em> first, and
    /// before this nothing could answer it: a node part-way through its attempt is indistinguishable
    /// from one nobody has started.
    /// </para>
    /// <para>
    /// <b>It is cleared when a pass begins</b>, because the executor is sequential and nothing is
    /// running when one starts. A run that died mid-step therefore reads <c>Planned</c> again rather
    /// than staying <c>Running</c> forever — the reconciliation a stored fact has to come with.
    /// </para>
    /// </remarks>
    public DateTimeOffset? AttemptStartedAt { get; init; }

    /// <summary>
    /// The last time this node actually ran and read the tree, and how much of it it saw.
    /// <see langword="null"/> when it has never run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Recorded even when nothing changed.</b> Everything else here is written only when the work
    /// produced something — a verdict, a dispute — so a node that ran, read its surroundings and had
    /// nothing to add left no trace at all. "Was this checked against the plan as it stands?" was
    /// therefore unanswerable, which is a question about drift rather than about outcomes.
    /// </para>
    /// <para>
    /// <b>One reading, not a history of them.</b> A log of every read is a transcript, and this
    /// design deliberately keeps no transcript store — what it keeps is the structure. The last
    /// reading plus <see cref="ReadCount"/> answers the drift question; a log would only answer
    /// questions this design has decided not to ask.
    /// </para>
    /// </remarks>
    public NodeReading? LastRead { get; init; }

    /// <summary>
    /// How many times this node has run <b>under the contract it now holds</b>. Cheap, and it
    /// distinguishes retried work from settled work.
    /// </summary>
    /// <remarks>
    /// <b>Attempts are spent against a contract, so a re-ruling starts them again</b> — the same line
    /// the tree already draws for verdicts, and for the same reason: a node handed different work has
    /// not attempted that work yet. Without it the iteration bound would make a re-ruling
    /// unactionable, since the node it re-rules could never run again. The count under the old
    /// contract is not lost: every earlier <see cref="PlanVersion"/> keeps its own copy of this node.
    /// </remarks>
    public int ReadCount { get; init; }

    /// <summary>
    /// The most recent verdict for each criterion — what currently holds, as opposed to everything
    /// that has ever been decided.
    /// </summary>
    /// <remarks>
    /// <see cref="Verdicts"/> accumulates and never overwrites, because the history is the evidence
    /// that a fix worked: a criterion that failed, was fixed and now passes reads very differently
    /// from one that simply passed. This is the same list collapsed to what is true now, and it is
    /// what status is derived from.
    /// </remarks>
    public IReadOnlyList<CriterionVerdict> LatestVerdicts =>
    [
        .. Verdicts
            .GroupBy(v => v.Criterion, StringComparer.Ordinal)
            .Select(g => g.Last())
    ];
}

/// <summary>
/// An attestation that a node ran against a particular state of the plan: what it was shown, and
/// what it said it did.
/// </summary>
/// <remarks>
/// <para>
/// The omission counts are the load-bearing part. The argument for reading a tree rather than
/// passing records is that a loss becomes recoverable — but that only holds if the node could have
/// read what it needed. When a node goes wrong, this separates the two cases: content that was
/// withheld from it, and content it was shown and did not use. The second is far worse news for the
/// design than the first, and without this it is indistinguishable from the first.
/// </para>
/// <para>
/// <b>One record per run, not two.</b> What the node was shown and what it then said belong
/// together: kept as separate fields on the node they could disagree about which run they describe,
/// which is the same objection this design makes to a status field.
/// </para>
/// </remarks>
public sealed record NodeReading
{
    /// <summary>When the node ran.</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>The plan version in force when it ran.</summary>
    public required int PlanVersion { get; init; }

    /// <summary>Estimated size of the view it was given.</summary>
    public required int ProjectedTokens { get; init; }

    /// <summary>Ancestors the projection did not describe.</summary>
    public int OmittedAncestors { get; init; }

    /// <summary>Records established elsewhere that the projection did not list.</summary>
    public int OmittedRecords { get; init; }

    /// <summary>
    /// The node's own account of what it did on this run, in its own words.
    /// <see langword="null"/> when it offered none — a shell check or a person need not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Kept because nothing else can reconstruct it.</b> Verdicts say which criteria held and a
    /// dispute says what contradicted the contract; neither says what the node actually did to find
    /// out. Whoever has to decide what a halted plan should become is reasoning about work it did not
    /// witness, and a decision made from the structure alone is a decision made from an inference.
    /// </para>
    /// <para>
    /// <b>Narration, and still never evidence.</b> It is what the node says, not what was checked —
    /// so nothing derives from it, no status depends on it, and it never becomes a verdict. It is
    /// shown to a reader and to a coordinator, both of whom are entitled to weigh it and neither of
    /// whom should mistake it for a ruling.
    /// </para>
    /// <para>
    /// <b>The last one, not a log of them.</b> Verdicts accumulate because the history is the
    /// evidence that a fix worked; narration does not, for the same reason this design keeps no
    /// transcript store.
    /// </para>
    /// </remarks>
    public string? Summary { get; init; }

    /// <summary>Whether the node saw the whole of its surroundings.</summary>
    public bool SawEverything => OmittedAncestors == 0 && OmittedRecords == 0;
}

/// <summary>The verdict of a check run against one acceptance criterion.</summary>
/// <remarks>
/// The oracle is named so that re-running it is a concrete instruction rather than an appeal to
/// re-verify somehow — the same reason the corpus records one against a verified entry, and the
/// reason the two shapes match: a node's verdict is what later *becomes* a verified record.
/// </remarks>
public sealed record CriterionVerdict
{
    /// <summary>The acceptance criterion this verdict is about, verbatim.</summary>
    public required string Criterion { get; init; }

    /// <summary>Whether the criterion held.</summary>
    public required bool Passed { get; init; }

    /// <summary>What decided it — a build, a test run, a named check.</summary>
    public required string Oracle { get; init; }

    /// <summary>
    /// What the oracle saw, when it can say. <see langword="null"/> for the ones that cannot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A deterministic check fills this from what it ran, never from what it thinks.</b> A failing
    /// <see cref="ProcessCheck"/> puts the command's own output here; a model that rules a criterion
    /// met puts its reasoning here — both are <em>evidence</em>, and both are kept for the same
    /// reason a change of plan is kept with its reason: a verdict without its grounds is a claim
    /// nobody can weigh.
    /// </para>
    /// <para>
    /// <b>Verbatim either way, and that is the line.</b> The oracle's own output, or a judge's own
    /// reasoning — never a third party's summary of either. See <see cref="Finding.Detail"/>, which
    /// is what gave a deterministic check something to put here at all.
    /// </para>
    /// </remarks>
    public string? Basis { get; init; }

    /// <summary>When it was decided.</summary>
    public required DateTimeOffset At { get; init; }
}

/// <summary>An attempt at a node that did not finish.</summary>
/// <remarks>
/// The message is whatever threw, <b>in its own words</b> — a provider's error, a tool's, a parser's.
/// It is the only account of why the attempt died, and rewriting it into something tidier would
/// replace evidence with a summary of evidence by something that was not there.
/// </remarks>
public sealed record NodeFailure
{
    /// <summary>What the failure said, verbatim.</summary>
    public required string Message { get; init; }

    /// <summary>When the attempt died.</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>Whether trying again could never help, however long anything waits.</summary>
    /// <remarks>
    /// <para>
    /// <b>An account out of allowance, not a busy one.</b> Both arrive as the same status code and
    /// only the provider's own words separate them, so the reading is taken once — where the
    /// exception still exists — and kept, rather than re-derived from this message by everything
    /// downstream that needs it.
    /// </para>
    /// <para>
    /// <b>A classification, never a count.</b> How many times a step has been attempted stays out of
    /// the tree on purpose (S9): a plan that could read it is a plan whose planner can reason about
    /// infrastructure. This says something different — that the halt cannot be answered by asking
    /// anybody in this run — and it is here so the loop can stop rather than escalate into seats that
    /// will fail the same way.
    /// </para>
    /// </remarks>
    public bool Terminal { get; init; }
}

/// <summary>
/// A choice a node has reached and may not make: what it needs decided, and what would answer it.
/// </summary>
/// <remarks>
/// <b>Every option must be one the work could actually take.</b> The step is asking because it has
/// already established that each of these is admissible; whoever answers is choosing between them,
/// not being asked to invent one.
/// </remarks>
public sealed record NodeQuestion
{
    /// <summary>What needs deciding, in a line somebody can answer.</summary>
    public required string Asks { get; init; }

    /// <summary>The admissible answers, as the step found them.</summary>
    public IReadOnlyList<string> Options { get; init; } = [];

    /// <summary>
    /// Whether the step did its task, so that each option meets its contract and choosing one settles
    /// the step. <see langword="false"/> when the options only come close.
    /// </summary>
    public bool Done { get; init; }

    /// <summary>When the node reached it.</summary>
    public required DateTimeOffset At { get; init; }
}

/// <summary>Where a step, or a whole plan, stands.</summary>
public enum StepState
{
    /// <summary>Not settled yet.</summary>
    Pending = 0,

    /// <summary>Settled on a result.</summary>
    Done,

    /// <summary>Stopped until somebody chooses between options, or the plan changes.</summary>
    Blocked,

    /// <summary>Given up, so nothing will attempt it again.</summary>
    Skipped
}

/// <summary>What a node was told when it asked. A decision, never a verdict.</summary>
public sealed record PlanAnswer
{
    /// <summary>The question, as it was asked.</summary>
    public required string Asked { get; init; }

    /// <summary>What was chosen.</summary>
    public required string Answer { get; init; }

    /// <summary>Who chose — a person, a role, an automation.</summary>
    public required string By { get; init; }

    /// <summary>When.</summary>
    public required DateTimeOffset At { get; init; }
}

/// <summary>
/// Somebody's decision that a node's work is given up. A decision, never a verdict.
/// </summary>
/// <remarks>
/// <b>Who and why are not decoration.</b> A step that is abandoned without them reads as a step that
/// failed, and the two lead somewhere completely different — one is a choice somebody made and can be
/// held to, the other is work that went wrong.
/// </remarks>
public sealed record PlanAbandonment
{
    /// <summary>Why the work is being given up.</summary>
    public required string Reason { get; init; }

    /// <summary>Who decided it — a person, a role, an automation.</summary>
    public required string By { get; init; }

    /// <summary>When it was decided.</summary>
    public required DateTimeOffset At { get; init; }
}

/// <summary>A node's report that its contract is wrong. A report, never a verdict.</summary>
/// <remarks>
/// Whether the contract really is wrong is not the node's call — it belongs to whoever authored the
/// criterion. What the node owns is the observation and the duty to return rather than route around it.
/// </remarks>
public sealed record PlanViolation
{
    /// <summary>The criterion the node believes is wrong or unmeetable.</summary>
    public required string Criterion { get; init; }

    /// <summary>What the node found that contradicts it.</summary>
    public required string Reason { get; init; }

    /// <summary>When it was reported.</summary>
    public required DateTimeOffset At { get; init; }
}

/// <summary>
/// What has happened to the attempt on a node: the same shape the workflow level already names.
/// </summary>
/// <remarks>
/// <para>
/// <b>A node is not a special kind of thing and does not need a special vocabulary.</b> It is work
/// that is going to be attempted, is being attempted, finished, died, or was given up — which is
/// <c>ExecutionStatus</c> one level down, and a reader who has seen one has seen both.
/// </para>
/// <para>
/// <b>This axis is about the attempt and says nothing about the contract.</b> Whether the work was
/// any good is <see cref="ContractOutcome"/>, and the two come apart in both directions: a node can be
/// <see cref="Completed"/> and <see cref="ContractOutcome.Unmet"/> — it ran and satisfied nothing — or
/// <see cref="Faulted"/> with criteria that already held. The single enum these replace returned
/// <c>Failed</c> for both an attempt that threw and a criterion that did not hold, which is a
/// distinction <see cref="PlanNode.Failure"/> is written to preserve.
/// </para>
/// </remarks>
public enum NodeLifecycle
{
    /// <summary>Nobody has attempted it.</summary>
    Planned = 0,

    /// <summary>An attempt is in flight. The only value that is stored rather than derived.</summary>
    Running,

    /// <summary>An attempt ran to completion, whatever it achieved.</summary>
    Completed,

    /// <summary>The last attempt threw: retries exhausted, a tool fault, a reply that would not parse.</summary>
    Faulted,

    /// <summary>
    /// The work is still in the plan and was given up, so nothing will attempt it again.
    /// </summary>
    /// <remarks>
    /// <b>Not the same as dropped, and both are wanted.</b> A <em>dropped</em> node is gone from this
    /// version of the plan because re-planning made the work unnecessary, and stays readable in every
    /// version before it (<see cref="PlanVersion.DroppedNodeIds"/>). An <em>abandoned</em> one is still
    /// here, and the record on it says who gave it up and why — so <em>what happened to this?</em> is
    /// answered by looking at it rather than by diffing two versions.
    /// </remarks>
    Abandoned
}

/// <summary>What the verdicts say about a node's contract. Derived, never stored.</summary>
/// <remarks>
/// <b>Three values, because a fourth would be a summary of the record rather than a fact about it.</b>
/// <em>Which</em> criteria are unmet, and whether they failed or were never decided, is in the
/// verdicts — where it is exact — and a status word that tried to carry it would be a worse copy of
/// something already written down.
/// </remarks>
public enum ContractOutcome
{
    /// <summary>Not every criterion holds, or not every child's does.</summary>
    Unmet = 0,

    /// <summary>Every criterion has a passing verdict, and every child is met.</summary>
    Met,

    /// <summary>
    /// The node reported that its contract is wrong, and is waiting on whoever authored it.
    /// </summary>
    /// <remarks>
    /// The same word as <c>PlanHaltCause.Disputed</c> and <see cref="PlanViolation"/>, which is the
    /// point: this state had three names and one meaning.
    /// </remarks>
    Disputed
}
